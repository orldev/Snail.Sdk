using Microsoft.CodeAnalysis;

namespace Snail.Sdk.Analyzers;

/// <summary>The rules of Snail.Sdk.Analyzers, each with the guideline it enforces.</summary>
/// <remarks>
/// The thresholds sit where the code of Toolkit/ and mcp/ is, as tools/Metrics measures it, so a rule flags the outliers
/// and not the habit. Severities live in Snail.Sdk.globalconfig; the descriptors default to warning so that a project
/// referencing the analyzers without the SDK is held to them too. Each rule's help link is its page on the site, and
/// AnalyzerReleases.Shipped.md lists the release that introduced it.
/// </remarks>
public static class Rules
{
    private const string Category = "Snail";

    private const string HelpPages = "https://orldev.github.io/Snail.Sdk/rules/";

    /// <summary>The most LINQ operators one chain may hold before it is unpacked into named steps.</summary>
    public const int MostQueryOperators = 4;

    /// <summary>How deep a lambda may sit inside lambdas before it is unpacked.</summary>
    public const int DeepestLambda = 2;

    /// <summary>The most lines an object built inside a return may span.</summary>
    public const int LongestReturnedObject = 15;

    /// <summary>The fewest statements of a method that owes a blank line before its final return.</summary>
    public const int StatementsThatBreathe = 4;

    /// <summary>S3L0001: a long query is unpacked into named steps.</summary>
    public static readonly DiagnosticDescriptor UnpackTheQuery = Rule(
        "S3L0001",
        "Unpack the query",
        "{0}: name the steps of this query with intermediate variables",
        $"A LINQ chain of more than {MostQueryOperators} operators, or a lambda nested {DeepestLambda} deep, reads as one breath; named steps say what each stage holds.");

    /// <summary>S3L0002: a large object is created, named, then returned.</summary>
    public static readonly DiagnosticDescriptor CreateThenReturn = Rule(
        "S3L0002",
        "Create, then return",
        "This object spans {0} lines inside the return: assign it to a named variable first",
        $"An object built across more than {LongestReturnedObject} lines inside a return hides the answer behind its construction.");

    /// <summary>S3L0003: a method of several statements has a blank line before its answer.</summary>
    public static readonly DiagnosticDescriptor LetItBreathe = Rule(
        "S3L0003",
        "Let it breathe",
        "Put a blank line before the final return: the answer is a phase of its own",
        $"In a method of {StatementsThatBreathe} statements or more, a blank line separates the answer from the work before it.");

    /// <summary>S3L0004: no comment lives inside a member body.</summary>
    public static readonly DiagnosticDescriptor NoInlineComments = Rule(
        "S3L0004",
        "No inline comments",
        "Remove this comment: a name or an extracted method says it",
        "Comments inside member bodies are forbidden; a block that needs one becomes a well-named private method.");

    /// <summary>S3L0005: the source is English.</summary>
    public static readonly DiagnosticDescriptor EnglishOnly = Rule(
        "S3L0005",
        "English only",
        "This {0} is not in English",
        "Names, documentation, comments and messages are professional technical English; a string an LLM must read verbatim lives in a Prompts type.");

    /// <summary>S3L0006: a name says what a thing is, not which pattern it follows.</summary>
    public static readonly DiagnosticDescriptor NoPatternNames = Rule(
        "S3L0006",
        "No pattern names",
        "'{0}' names a pattern ('{1}'), not what the thing is",
        "No Service, Manager, Helper, Processor or Data suffix, and no Helpers, Utils, Managers, Common or Shared folders.");

    /// <summary>S3L0007: a name does not repeat its type.</summary>
    public static readonly DiagnosticDescriptor NoTypesInNames = Rule(
        "S3L0007",
        "No types in names",
        "'{0}' repeats its type ('{1}'): name what it holds",
        "users, never userList or userDict.");

    /// <summary>S3L0008: a boolean reads as a question.</summary>
    public static readonly DiagnosticDescriptor BooleansAsk = Rule(
        "S3L0008",
        "Booleans ask",
        "'{0}' is a boolean that does not read as a question (Is, Has, Can, Should…)",
        "Booleans and boolean-returning methods sound like yes/no questions, so that a condition reads as plain English.");

    /// <summary>S3L0009: no this. where nothing is shadowed.</summary>
    public static readonly DiagnosticDescriptor NoThis = Rule(
        "S3L0009",
        "No this qualifier",
        "Remove 'this.': nothing here is named '{0}' but the member",
        "IDE0003, which the build does not run: this. is written only where a local or a parameter shadows the member.");

    /// <summary>S3L0010: keyword types where the keyword exists.</summary>
    public static readonly DiagnosticDescriptor KeywordTypes = Rule(
        "S3L0010",
        "Keyword types",
        "Use '{0}' rather than '{1}'",
        "IDE0049, which the build does not run: int, string, bool and the other keywords rather than their framework names.");

    private static DiagnosticDescriptor Rule(string id, string title, string message, string description) =>
        new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description, $"{HelpPages}{id}/");
}
