using System.Text.Json;

namespace Snail.Sdk.Conformance;

/// <summary>
/// The shared files a repository is compared with, as this release of Snail.Sdk keeps them: global.json's sdk section,
/// the .editorconfig, and the shared workflows and actions by the path a caller names them with
/// (.github/workflows/lib-ci.yml, .github/actions/nuget-push).
/// </summary>
internal sealed record Standard(JsonElement Sdk, string EditorConfig, IReadOnlyDictionary<string, Workflow> Shared)
{
    private const string SharedPrefix = "shared/";

    private const string ActionFile = "/action.yml";

    /// <summary>The files embedded in the tool.</summary>
    public static Standard Embedded()
    {
        using var global = JsonDocument.Parse(Resource("global.json"), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var shared = typeof(Standard).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(SharedPrefix, StringComparison.Ordinal))
            .ToDictionary(Target, name => Workflow.Parse(Resource(name)), StringComparer.Ordinal);

        return new Standard(global.RootElement.GetProperty("sdk").Clone(), Resource("editorconfig"), shared);
    }

    /// <summary>What a caller names a shared file by: a workflow by its file, an action by its folder.</summary>
    private static string Target(string resource)
    {
        var path = resource[SharedPrefix.Length..].Replace('\\', '/');

        return path.EndsWith(ActionFile, StringComparison.Ordinal) ? path[..^ActionFile.Length] : path;
    }

    private static string Resource(string name)
    {
        using var stream = typeof(Standard).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"the tool was built without its {name}");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
