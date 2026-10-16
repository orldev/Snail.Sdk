namespace Snail.Sdk.Docs;

/// <summary>
/// What stops the generator, with the id the build reports it under: S3LD0001 docs/tools.json is not valid, S3LD0002
/// docs/tools.json and the server disagree, S3LD0003 an input cannot be read or an output written.
/// </summary>
internal sealed class DocsError(string code, string message) : InvalidOperationException(message)
{
    /// <summary>docs/tools.json is not valid: not JSON, an unknown key, a slug or title that cannot be a page.</summary>
    public const string InvalidSettings = "S3LD0001";

    /// <summary>docs/tools.json and the server disagree: a class without a family, a family without its class, two pages of one slug.</summary>
    public const string SettingsAndServerDisagree = "S3LD0002";

    /// <summary>An input cannot be read or an output written.</summary>
    public const string Unreadable = "S3LD0003";

    /// <summary>The id the build reports the error under.</summary>
    public string Code { get; } = code;

    /// <summary>The error as one MSBuild line: error, the id, what is wrong.</summary>
    public static string Line(string code, string message) => $"error {code}: {message}";
}
