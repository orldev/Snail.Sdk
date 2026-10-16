using System.Collections.Frozen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Usage: metrics <folder> [<folder> ...] — every .cs file below them, generated code and build output left out.
var linqOperators = FrozenSet.Create(StringComparer.Ordinal,
    "Select", "SelectMany", "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "GroupBy", "Join",
    "GroupJoin", "Distinct", "DistinctBy", "Union", "Intersect", "Except", "Concat", "Zip", "Skip", "SkipWhile", "Take",
    "TakeWhile", "Any", "All", "Count", "LongCount", "Sum", "Min", "Max", "MinBy", "MaxBy", "Average", "Aggregate", "First",
    "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault", "ElementAt", "ToList", "ToArray",
    "ToDictionary", "ToHashSet", "ToLookup", "Cast", "OfType", "Reverse", "Chunk", "CountBy", "AggregateBy", "Index",
    "Prepend", "Append", "DefaultIfEmpty", "SequenceEqual", "Contains", "AsEnumerable", "ToFrozenSet", "ToFrozenDictionary");

var samples = new Dictionary<string, List<(int Value, string Where)>>(StringComparer.Ordinal);
var files = args
    .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
    .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
    .Where(path => !path.EndsWith(".g.cs", StringComparison.Ordinal) && !path.EndsWith(".Designer.cs", StringComparison.Ordinal))
    .ToList();

foreach (var path in files)
{
    var part = path.Contains("/tests/", StringComparison.Ordinal) || path.Contains("/benchmarks/", StringComparison.Ordinal) ? "tests" : "src";
    var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();

    foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(IsChainTop))
    {
        var operators = ChainOperators(invocation).Count(linqOperators.Contains);

        if (operators > 0)
            Record($"{part}: LINQ operators in one chain", operators, invocation);
    }

    foreach (var lambda in root.DescendantNodes().OfType<LambdaExpressionSyntax>().Where(lambda => !lambda.Ancestors().OfType<LambdaExpressionSyntax>().Any()))
    {
        var depth = lambda.DescendantNodes().OfType<LambdaExpressionSyntax>().Select(inner => inner.Ancestors().TakeWhile(node => node != lambda).OfType<LambdaExpressionSyntax>().Count() + 1).DefaultIfEmpty(0).Max();

        Record($"{part}: lambdas nested inside a lambda", depth, lambda);
    }

    foreach (var body in root.DescendantNodes().OfType<BlockSyntax>().Where(block => block.Parent is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax))
    {
        Record($"{part}: statements at the top of a method body", body.Statements.Count, body);
        Record($"{part}: lines of a method body", Lines(body), body);

        if (body.Statements.Count >= 2 && body.Statements[^1] is ReturnStatementSyntax final)
        {
            var previousEnd = body.Statements[^2].GetLocation().GetLineSpan().EndLinePosition.Line;
            var finalStart = final.GetLocation().GetLineSpan().StartLinePosition.Line;

            Record($"{part}: blank line before the final return (1 = yes), methods of {Bucket(body.Statements.Count)} statements", finalStart - previousEnd > 1 ? 1 : 0, final);
        }
    }

    foreach (var returned in root.DescendantNodes().Select(Returned).OfType<ExpressionSyntax>())
    {
        if (returned is BaseObjectCreationExpressionSyntax or AnonymousObjectCreationExpressionSyntax or CollectionExpressionSyntax && Lines(returned) > 1)
            Record($"{part}: lines of an object built inside return", Lines(returned), returned);
    }
}

Console.WriteLine($"{files.Count} files");

foreach (var (metric, values) in samples.OrderBy(pair => pair.Key, StringComparer.Ordinal))
{
    var sorted = values.OrderBy(value => value.Value).ToList();

    Console.WriteLine($"""

        {metric}: n={sorted.Count} p50={Percentile(0.50)} p75={Percentile(0.75)} p90={Percentile(0.90)} p95={Percentile(0.95)} p99={Percentile(0.99)} max={sorted[^1].Value} mean={sorted.Average(value => value.Value):0.00}
        """);

    var tail = sorted.Where(value => value.Value > Percentile(0.90)).CountBy(value => value.Value).OrderBy(pair => pair.Key);

    Console.WriteLine($"    above p90: {string.Join(", ", tail.Select(pair => $"{pair.Key}×{pair.Value}"))}");

    foreach (var (value, where) in sorted.TakeLast(3).Reverse())
        Console.WriteLine($"    {value,4}  {where}");

    int Percentile(double share) => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(share * sorted.Count) - 1)].Value;
}

void Record(string metric, int value, SyntaxNode node)
{
    if (!samples.TryGetValue(metric, out var values))
        samples[metric] = values = [];

    var line = node.GetLocation().GetLineSpan();

    values.Add((value, $"{line.Path.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~", StringComparison.Ordinal)}:{line.StartLinePosition.Line + 1}"));
}

static bool IsChainTop(InvocationExpressionSyntax invocation) =>
    invocation.Parent is not MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax } and not ConditionalAccessExpressionSyntax;

static IEnumerable<string> ChainOperators(InvocationExpressionSyntax invocation)
{
    ExpressionSyntax? current = invocation;

    while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
    {
        yield return access.Name.Identifier.Text;

        current = access.Expression;
    }
}

static int Lines(SyntaxNode node)
{
    var span = node.GetLocation().GetLineSpan();

    return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
}

static string Bucket(int statements) => statements switch
{
    <= 3 => "2-3",
    <= 6 => "4-6",
    <= 10 => "7-10",
    _ => "11+"
};

static ExpressionSyntax? Returned(SyntaxNode node) => node switch
{
    ReturnStatementSyntax ret => ret.Expression,
    YieldStatementSyntax yield => yield.Expression,
    ArrowExpressionClauseSyntax arrow => arrow.Expression,
    _ => null
};
