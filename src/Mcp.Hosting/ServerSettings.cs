using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>A server's settings: its settings file, then its <c>SNAIL_MCP_&lt;NAME&gt;_</c> variables, which win.</summary>
/// <remarks>
/// <para>
/// One chain for every server, so a person who has set up one knows where every other looks. A file named in
/// <c>SNAIL_MCP_&lt;NAME&gt;_CONFIG</c> is the only one: when it is missing, no file is read rather than another one the
/// person did not mean. Otherwise the first that exists of <c>.s3l-mcp-&lt;name&gt;.json</c> and
/// <c>s3l-mcp-&lt;name&gt;.json</c> in the working directory, <c>~/.config/s3l-mcp-&lt;name&gt;/config.json</c> and
/// <c>~/.s3l-mcp-&lt;name&gt;.json</c> is read, and only that one. The names from before the rename are not read.
/// </para>
/// <para>
/// The settings file is where a setting of one machine goes — a token above all. A secret in a launchd job or in a
/// client's registration travels wherever that file is copied, and a job edited by hand is no longer restarted by an
/// install; the settings file, readable by its owner alone, serves stdio sessions and a shared server alike.
/// </para>
/// <para>
/// The settings are read before a host exists, since a server decides between stdio and HTTP from them, and
/// <see cref="McpHost.CreateBuilder"/> adds the same sources to the host it builds when the program names its server.
/// </para>
/// </remarks>
public static class ServerSettings
{
    /// <summary>The prefix of the server's variables: <c>SNAIL_MCP_&lt;NAME&gt;_</c>.</summary>
    /// <param name="name">The server's short name, such as figma.</param>
    public static string PrefixOf(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return $"SNAIL_MCP_{name.ToUpperInvariant()}_";
    }

    /// <summary>The variable that names the settings file outright: <c>SNAIL_MCP_&lt;NAME&gt;_CONFIG</c>.</summary>
    public static string FileVariableOf(string name) => $"{PrefixOf(name)}CONFIG";

    /// <summary>The files looked for, in order; the first that exists is the server's settings file, and a file the variable names is the only one.</summary>
    public static IReadOnlyList<string> Candidates(string name) =>
        Candidates(name, Directory.GetCurrentDirectory(), ServerFolders.Home, Environment.GetEnvironmentVariable(FileVariableOf(name)));

    /// <summary>The chain for a working directory, a home and the path the variable names, if it names one.</summary>
    internal static IReadOnlyList<string> Candidates(string name, string workingDirectory, string home, string? named)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!string.IsNullOrWhiteSpace(named))
            return [ExpandHome(named.Trim(), home)];

        return
        [
            Path.Combine(workingDirectory, $".s3l-mcp-{name}.json"),
            Path.Combine(workingDirectory, $"s3l-mcp-{name}.json"),
            Path.Combine(home, ".config", $"s3l-mcp-{name}", "config.json"),
            Path.Combine(home, $".s3l-mcp-{name}.json"),
        ];
    }

    /// <summary>The settings file the server reads, or <c>null</c> when it has none.</summary>
    public static string? FileOf(string name) => Candidates(name).FirstOrDefault(File.Exists);

    /// <summary>The server's settings as they stand now: its settings file, then its variables.</summary>
    public static IConfigurationRoot Read(string name) => new ConfigurationBuilder().AddServerSettings(name).Build();

    /// <summary>The server's short name, from the command Snail.Sdk.Mcp writes into its assembly; <c>null</c> for a program built otherwise.</summary>
    public static string? NameOf(Assembly? server)
    {
        var command = server?.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(metadata => metadata.Key == ServerIdentity.NameKey)?.Value;

        return command is not null && command.StartsWith(CommandPrefix, StringComparison.Ordinal) && command.Length > CommandPrefix.Length
            ? command[CommandPrefix.Length..]
            : null;
    }

    private const string CommandPrefix = "s3l-mcp-";

    extension(IConfigurationBuilder configuration)
    {
        /// <summary>Adds the server's settings file, when it has one, and then its variables, which win over the file.</summary>
        /// <param name="name">The server's short name, such as figma.</param>
        public IConfigurationBuilder AddServerSettings(string name) =>
            configuration.AddServerSettings(name, FileOf(name), Environment.GetEnvironmentVariables());

        /// <summary>Adds the given settings file, when there is one, and then the server's variables among <paramref name="variables"/>.</summary>
        public IConfigurationBuilder AddServerSettings(string name, string? file, IDictionary variables)
        {
            ArgumentNullException.ThrowIfNull(variables);

            if (file is not null)
                configuration.AddJsonFile(Path.GetFullPath(file), optional: true, reloadOnChange: false);

            return configuration.AddInMemoryCollection(EnvironmentSettings.Read(PrefixOf(name), variables));
        }
    }

    private static string ExpandHome(string path, string home) =>
        path == "~" ? home
        : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, path[2..])
        : path;
}
