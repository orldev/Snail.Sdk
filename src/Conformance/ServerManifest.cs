using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snail.Sdk.Conformance;

/// <summary>
/// The check of .mcp/server.json, the server's entry in the MCP registry. Whatever the file holds, the check answers
/// with what it saw: a missing or mistyped part is a finding, never an exception.
/// </summary>
internal static partial class ServerManifest
{
    private const string ManifestPath = ".mcp/server.json";

    private const string Unreleased = "0.0.0-dev";

    /// <summary>The longest description the registry's schema accepts.</summary>
    private const int LongestDescription = 100;

    private const string Stdio = "stdio";

    private const string StreamableHttp = "streamable-http";

    /// <summary>The finding on the repository's server.json.</summary>
    public static Finding Of(Repository repository)
    {
        if (repository.Text(ManifestPath) is null)
            return Rules.ServerManifest.Unmet($"no {ManifestPath}");

        if (repository.Json(ManifestPath) is not { ValueKind: JsonValueKind.Object } manifest)
            return Rules.ServerManifest.Unmet($"{ManifestPath} is not a JSON object");

        var problems = Problems(repository, manifest).ToList();

        return problems.Count == 0
            ? Rules.ServerManifest.Met()
            : Rules.ServerManifest.Unmet(string.Join("; ", problems));
    }

    /// <summary>The projects that are the server: on a Snail.Sdk.Mcp level and of no kind.</summary>
    public static IEnumerable<string> ServerProjects(Repository repository) =>
        repository.Projects.Where(project => Repository.SdkOf(project).StartsWith("Snail.Sdk.Mcp", StringComparison.Ordinal) && Repository.KindOf(project).Length == 0);

    private static IEnumerable<string> Problems(Repository repository, JsonElement manifest)
    {
        var schema = Text(manifest, "$schema");
        var name = Text(manifest, "name");
        var version = Text(manifest, "version");

        if (schema != Checks.ServerSchema)
            yield return schema is null ? $"no $schema: {Checks.ServerSchema}" : $"$schema is {schema}, not {Checks.ServerSchema}";

        if (name is null)
            yield return "no name";
        else if (!RegistryName().IsMatch(name) || !name.EndsWith($"-{repository.ShortName}", StringComparison.Ordinal))
            yield return $"name {name} is not <reverse DNS>/<server> ending in -{repository.ShortName}";

        if (Text(manifest, "description") is { Length: > LongestDescription } description)
            yield return $"description is {description.Length} characters, the registry takes {LongestDescription}";

        if (version is null)
            yield return "no version";
        else if (version.Length == 0 || version == Unreleased)
            yield return $"version is \"{version}\": write the version being released";

        if (!manifest.TryGetProperty("packages", out var listed) || listed.ValueKind is not JsonValueKind.Array || listed.GetArrayLength() == 0)
        {
            yield return "no packages";
            yield break;
        }

        foreach (var problem in PackageProblems(repository, [.. listed.EnumerateArray()]))
            yield return problem;
    }

    private static IEnumerable<string> PackageProblems(Repository repository, List<JsonElement> packages)
    {
        if (packages.Any(package => package.ValueKind is not JsonValueKind.Object))
        {
            yield return "a package that is not an object";
            yield break;
        }

        var identifiers = packages.Select(package => Text(package, "identifier")).ToList();
        var strangers = identifiers.Where(identifier => identifier != repository.Name).Select(identifier => identifier ?? "nothing").Distinct(StringComparer.Ordinal).ToList();

        if (strangers.Count > 0)
            yield return $"names the package {string.Join(", ", strangers)}, not {repository.Name}";

        var declared = packages.Select(Transport).ToList();
        var expected = ExpectedTransports(repository);

        if (declared.Any(transport => transport is null))
            yield return "a package without transport.type";
        else if (expected is not null && !declared.OfType<string>().ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            yield return $"transports {string.Join(", ", declared.Distinct(StringComparer.Ordinal))}, but SnailMcpTransport asks for {string.Join(", ", expected)}";

        var prefix = $"SNAIL_MCP_{repository.ShortName.ToUpperInvariant()}_";
        var variables = packages.SelectMany(Variables).ToList();
        var strays = variables.Where(variable => !variable.StartsWith(prefix, StringComparison.Ordinal)).Select(variable => variable.Length == 0 ? "a variable without a name" : variable).ToList();

        if (strays.Count > 0)
            yield return $"variables without {prefix}: {string.Join(", ", strays)}";
    }

    /// <summary>The transports a client finds the server by: stdio, streamable HTTP, or both, as SnailMcpTransport builds it; null when no server project says.</summary>
    private static string[]? ExpectedTransports(Repository repository)
    {
        var server = ServerProjects(repository).FirstOrDefault();

        if (server is null)
            return null;

        return (Repository.Setting(server, "SnailMcpTransport") ?? "Stdio").ToLowerInvariant() switch
        {
            "http" => [StreamableHttp],
            "both" => [Stdio, StreamableHttp],
            _ => [Stdio]
        };
    }

    private static string? Transport(JsonElement package) =>
        package.TryGetProperty("transport", out var transport) && transport.ValueKind is JsonValueKind.Object ? Text(transport, "type") : null;

    private static IEnumerable<string> Variables(JsonElement package)
    {
        if (!package.TryGetProperty("environmentVariables", out var listed) || listed.ValueKind is not JsonValueKind.Array)
            return [];

        return listed.EnumerateArray().Select(variable => variable.ValueKind is JsonValueKind.Object ? Text(variable, "name") ?? string.Empty : string.Empty);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"^[a-zA-Z0-9.-]+/[a-zA-Z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex RegistryName();
}
