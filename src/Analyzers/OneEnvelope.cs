using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Snail.Sdk.Analyzers;

/// <summary>S3L0011: a tool of an MCP server answers in the one envelope, <c>{"ok":true,"data":…}</c> or <c>{"ok":false,"error":…}</c>.</summary>
/// <remarks>
/// <para>
/// A method marked <c>[McpServerTool]</c> is held to the shapes the envelope comes in: a <c>CallToolResult</c> — or a
/// task of one — that <c>ToolAnswer</c> or <c>JobAnswer</c> builds, or a string that <c>ToolAnswer.OkText</c>,
/// <c>FailedText</c> or <c>Text</c> writes (Snail.MCP.Figma's 136 string tools). It reports what is certain not to be the
/// envelope: a tool that answers nothing (<c>void</c>, <c>Task</c>), one that answers a record, a <c>JsonNode</c>, a
/// number or content blocks the MCP SDK serializes as they are, a <c>CallToolResult</c> built in its return with
/// <c>new</c>, and a string tool whose return is text written in place — a literal, an interpolation, a concatenation,
/// <c>string.Empty</c>, <c>string.Format</c>, <c>Concat</c> or <c>Join</c>, a <c>ToString()</c>, <c>JsonSerializer.Serialize</c>
/// or <c>JsonNode.ToJsonString</c>, each also through <c>?:</c>, <c>??</c>, a switch expression or <c>Task.FromResult</c>.
/// </para>
/// <para>
/// What it cannot see it leaves alone, rather than flag a tool that answers well: a string or a result handed over by a
/// method of the server's own (Figma's <c>ExecuteAsync</c>), a local variable, a returned <c>object</c>, a type parameter,
/// and <c>Envelope&lt;T&gt;</c>, the envelope as a type. A test's tools are doubles and spared by the test projects'
/// configuration. Without the MCP SDK referenced the rule does nothing.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneEnvelope : DiagnosticAnalyzer
{
    private const string Nothing = "nothing";

    private const string PlainText = "plain text";

    private const string HandBuilt = "a CallToolResult built by hand";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rules.AnswerInTheEnvelope];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(Start);
    }

    private static void Start(CompilationStartAnalysisContext context)
    {
        if (Known.Of(context.Compilation) is not { } known)
            return;

        context.RegisterSyntaxNodeAction(node => Analyze(node, known), SyntaxKind.MethodDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, Known known)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;

        if (declaration.AttributeLists.Count == 0 || context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not { } method)
            return;

        if (!method.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, known.Tool)))
            return;

        var answered = known.Answered(method.ReturnType);

        if (answered is null)
        {
            Report(context, declaration.ReturnType, method, Nothing);

            return;
        }

        if (SymbolEqualityComparer.Default.Equals(answered, known.Result))
            ReportReturns(context, declaration, method, known, IsHandBuilt, HandBuilt);
        else if (answered.SpecialType is SpecialType.System_String)
            ReportReturns(context, declaration, method, known, IsPlainText, PlainText);
        else if (!known.IsUnknown(answered))
            Report(context, declaration.ReturnType, method, $"'{answered.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'");
    }

    private static void ReportReturns(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax declaration,
        IMethodSymbol method,
        Known known,
        Func<ExpressionSyntax, SemanticModel, Known, bool> isWrong,
        string what)
    {
        foreach (var returned in Returned(declaration))
        {
            foreach (var leaf in Leaves(returned, context.SemanticModel, known))
            {
                if (isWrong(leaf, context.SemanticModel, known))
                    Report(context, leaf, method, what);
            }
        }
    }

    private static void Report(SyntaxNodeAnalysisContext context, SyntaxNode node, IMethodSymbol method, string what) =>
        context.ReportDiagnostic(Diagnostic.Create(Rules.AnswerInTheEnvelope, node.GetLocation(), method.Name, what));

    /// <summary>The expressions the method itself returns: its expression body, or its return statements outside lambdas and local functions.</summary>
    private static IEnumerable<ExpressionSyntax> Returned(MethodDeclarationSyntax declaration)
    {
        if (declaration.ExpressionBody is { } arrow)
            return [arrow.Expression];

        if (declaration.Body is null)
            return [];

        var statements = declaration.Body.DescendantNodes(node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

        return statements.OfType<ReturnStatementSyntax>().Select(statement => statement.Expression).OfType<ExpressionSyntax>();
    }

    /// <summary>The expressions an answer may be, through parentheses, <c>?:</c>, <c>??</c>, a switch expression, <c>!</c> and <c>Task.FromResult</c>.</summary>
    private static IEnumerable<ExpressionSyntax> Leaves(ExpressionSyntax expression, SemanticModel model, Known known) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => Leaves(parenthesized.Expression, model, known),
        PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed => Leaves(suppressed.Operand, model, known),
        ConditionalExpressionSyntax conditional => Leaves(conditional.WhenTrue, model, known).Concat(Leaves(conditional.WhenFalse, model, known)),
        BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesced => Leaves(coalesced.Left, model, known).Concat(Leaves(coalesced.Right, model, known)),
        SwitchExpressionSyntax switched => switched.Arms.SelectMany(arm => Leaves(arm.Expression, model, known)),
        InvocationExpressionSyntax { ArgumentList.Arguments.Count: 1 } invocation when known.IsFromResult(Invoked(invocation, model)) =>
            Leaves(invocation.ArgumentList.Arguments[0].Expression, model, known),
        _ => [expression]
    };

    private static bool IsHandBuilt(ExpressionSyntax leaf, SemanticModel model, Known known) =>
        leaf is BaseObjectCreationExpressionSyntax && SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(leaf).Type, known.Result);

    private static bool IsPlainText(ExpressionSyntax leaf, SemanticModel model, Known known) => leaf switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
        InterpolatedStringExpressionSyntax => true,
        BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } => model.GetTypeInfo(leaf).Type?.SpecialType is SpecialType.System_String,
        MemberAccessExpressionSyntax => model.GetSymbolInfo(leaf).Symbol is IFieldSymbol { Name: "Empty", ContainingType.SpecialType: SpecialType.System_String },
        InvocationExpressionSyntax invocation => known.IsTextWritten(Invoked(invocation, model)),
        _ => false
    };

    private static IMethodSymbol? Invoked(InvocationExpressionSyntax invocation, SemanticModel model) =>
        model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

    /// <summary>The types the rule tells answers by, as this compilation knows them.</summary>
    private sealed class Known
    {
        private static readonly ImmutableHashSet<string> StringWriters = ImmutableHashSet.Create("Concat", "Format", "Join");

        private Known(Compilation compilation, INamedTypeSymbol tool, INamedTypeSymbol result)
        {
            Tool = tool;
            Result = result;
            Task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
            TaskOf = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
            ValueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
            ValueTaskOf = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
            Envelope = compilation.GetTypeByMetadataName("Snail.Sdk.Mcp.Hosting.Answers.Envelope`1");
            Serializer = compilation.GetTypeByMetadataName("System.Text.Json.JsonSerializer");
            Node = compilation.GetTypeByMetadataName("System.Text.Json.Nodes.JsonNode");
        }

        /// <summary><c>[McpServerTool]</c>.</summary>
        public INamedTypeSymbol Tool { get; }

        /// <summary><c>CallToolResult</c>.</summary>
        public INamedTypeSymbol Result { get; }

        private INamedTypeSymbol? Task { get; }

        private INamedTypeSymbol? TaskOf { get; }

        private INamedTypeSymbol? ValueTask { get; }

        private INamedTypeSymbol? ValueTaskOf { get; }

        private INamedTypeSymbol? Envelope { get; }

        private INamedTypeSymbol? Serializer { get; }

        private INamedTypeSymbol? Node { get; }

        /// <summary>The types the rule needs, or null when the compilation does not reference the MCP SDK.</summary>
        public static Known? Of(Compilation compilation)
        {
            var tool = compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerToolAttribute");
            var result = compilation.GetTypeByMetadataName("ModelContextProtocol.Protocol.CallToolResult");

            return tool is null || result is null ? null : new Known(compilation, tool, result);
        }

        /// <summary>What a tool answers: its return type, out of a task; null when it answers nothing.</summary>
        public ITypeSymbol? Answered(ITypeSymbol returned)
        {
            if (returned.SpecialType is SpecialType.System_Void || Is(returned, Task) || Is(returned, ValueTask))
                return null;

            var isTask = returned is INamedTypeSymbol { IsGenericType: true } generic && (Is(generic.OriginalDefinition, TaskOf) || Is(generic.OriginalDefinition, ValueTaskOf));

            return isTask ? ((INamedTypeSymbol)returned).TypeArguments[0] : returned;
        }

        /// <summary>Whether the rule cannot tell what such an answer holds: an object, a type parameter, dynamic, an error, or the envelope as a type.</summary>
        public bool IsUnknown(ITypeSymbol answered) =>
            answered.SpecialType is SpecialType.System_Object
            || answered.TypeKind is TypeKind.TypeParameter or TypeKind.Dynamic or TypeKind.Error
            || Is(answered.OriginalDefinition, Envelope);

        /// <summary>Whether the method is <c>Task.FromResult</c> or <c>ValueTask.FromResult</c>.</summary>
        public bool IsFromResult(IMethodSymbol? method) =>
            method is { Name: "FromResult" } && (Is(method.ContainingType, Task) || Is(method.ContainingType, ValueTask));

        /// <summary>Whether the method writes text in place: <c>string.Format</c>, <c>Concat</c> or <c>Join</c>, a <c>ToString()</c>, <c>JsonSerializer.Serialize</c>, <c>JsonNode.ToJsonString</c>.</summary>
        public bool IsTextWritten(IMethodSymbol? method)
        {
            if (method is null || method.ReturnType.SpecialType is not SpecialType.System_String)
                return false;

            var isStringWriter = method.ContainingType.SpecialType is SpecialType.System_String && StringWriters.Contains(method.Name);
            var isSerialized = method.Name == "Serialize" && Is(method.ContainingType, Serializer);
            var isNodeText = method.Name == "ToJsonString" && IsOrDerives(method.ContainingType, Node);

            return isStringWriter || method.Name == "ToString" || isSerialized || isNodeText;
        }

        private static bool Is(ITypeSymbol? type, INamedTypeSymbol? known) =>
            known is not null && SymbolEqualityComparer.Default.Equals(type, known);

        private static bool IsOrDerives(ITypeSymbol? type, INamedTypeSymbol? known)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (Is(current, known))
                    return true;
            }

            return false;
        }
    }
}
