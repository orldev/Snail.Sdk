using System.ComponentModel;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>A server under test, started as a program the way a client starts it.</summary>
/// <remarks>
/// Only the process shows what a native build gets wrong: a parameter or result type it was not told about fails when
/// the tool is listed or called, not when the program is built, and only the process shows a log line that strayed
/// onto stdout. So a contract test calls every tool over stdio — against the dll beside the tests by default, and
/// against a published native program when a variable names it: in CI after each native build, and on a developer's
/// machine against <c>~/.s3l-mcp/&lt;name&gt;/current</c>.
/// </remarks>
public static class ServerProgram
{
    private static readonly Lazy<string> OwnHome = new(TestFolder.Create);

    /// <summary>
    /// What starts the server: the program the variable names — through <c>dotnet</c> when it is a framework-dependent
    /// dll — or <c>dotnet &lt;assembly&gt;.dll</c> beside the tests.
    /// </summary>
    public static (string Command, string[] Arguments) Command(string assemblyName, string overrideVariable) =>
        Environment.GetEnvironmentVariable(overrideVariable) switch
        {
            { Length: > 0 } dll when dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) => ("dotnet", [dll]),
            { Length: > 0 } native => (native, []),
            _ => ("dotnet", [Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll")])
        };

    /// <summary>
    /// The home folder a server under test gets unless the test names one: empty and its own, so the server reads none of
    /// the developer's settings and writes nothing into their data.
    /// </summary>
    /// <remarks>
    /// One <see cref="TestFolder"/> for the test process, made on first use and deleted with whatever the servers wrote
    /// into it when the process exits; one the process left behind is proven a leftover by its name.
    /// </remarks>
    public static string Home => OwnHome.Value;

    /// <summary>
    /// A settings file that never exists, for <c>SNAIL_MCP_&lt;SHORT&gt;_CONFIG</c> of a server under test: a file the
    /// variable names is the only one the server reads, so it reads none — not even one in the working directory.
    /// </summary>
    /// <remarks>A test that starts the server's process itself sets it; <c>StartAsync</c> sets it unless the test names a file.</remarks>
    public static string NoSettingsFile => Path.Combine(Home, "no-settings-file.json");

    /// <summary>
    /// How long a server under test may take to answer <c>initialize</c> before the test fails naming it. A server that
    /// starts, keeps its pipes open and never answers — a native host deadlocked at start — is given up on by the MCP
    /// SDK after its own 60 seconds, with a message that names no program; this is that timeout, long enough for a
    /// native build's first start on a cold runner, and the failure says which program hung. A server whose stdout
    /// closes is noticed within seconds and needs no patience.
    /// </summary>
    internal static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The server started and connected, with these variables in its environment, <see cref="Home"/> as its home and
    /// <see cref="NoSettingsFile"/> as its settings file.
    /// </summary>
    /// <exception cref="TimeoutException">The server did not answer <c>initialize</c> within <see cref="Patience"/>.</exception>
    public static Task<McpClient> StartAsync(
        string assemblyName,
        string overrideVariable,
        IDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default) =>
        StartAsync(assemblyName, overrideVariable, environment, Patience, cancellationToken);

    /// <summary>The server started with a patience of the caller's own, for a test of the patience itself.</summary>
    internal static async Task<McpClient> StartAsync(
        string assemblyName,
        string overrideVariable,
        IDictionary<string, string?>? environment,
        TimeSpan patience,
        CancellationToken cancellationToken)
    {
        var (command, arguments) = Command(assemblyName, overrideVariable);
        var variables = Variables(assemblyName, environment);
        var transport = new StdioClientTransport(new StdioClientTransportOptions { Command = command, Arguments = arguments, EnvironmentVariables = variables });
        var options = new McpClientOptions { InitializationTimeout = patience };

        try
        {
            return await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException silent)
        {
            throw new TimeoutException($"{command} {string.Join(' ', arguments)} did not answer initialize within {patience.TotalSeconds:0} s", silent);
        }
    }

    /// <summary>The environment a server under test starts with: its own home and no settings file, then what the test names.</summary>
    internal static Dictionary<string, string?> Variables(string assemblyName, IDictionary<string, string?>? environment)
    {
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal) { ["HOME"] = Home, ["USERPROFILE"] = Home };

        if (ShortNameOf(assemblyName) is { } name)
            variables[$"SNAIL_MCP_{name.ToUpperInvariant()}_CONFIG"] = NoSettingsFile;

        foreach (var (key, value) in environment ?? new Dictionary<string, string?>())
            variables[key] = value;

        return variables;
    }

    /// <summary><c>Snail.MCP.CodeContext</c> is <c>codecontext</c>: the rule every server's command, job and variables share.</summary>
    private static string? ShortNameOf(string assemblyName) =>
        assemblyName.StartsWith(ServerPrefix, StringComparison.OrdinalIgnoreCase) && assemblyName.Length > ServerPrefix.Length
            ? assemblyName[ServerPrefix.Length..].Replace(".", string.Empty, StringComparison.Ordinal).ToLowerInvariant()
            : null;

    private const string ServerPrefix = "Snail.MCP.";

    /// <summary>The text of a tool's answer; the same as <c>answer.Text()</c> of <see cref="ToolAnswers"/>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static string Text(CallToolResult answer) => answer.Text();
}
