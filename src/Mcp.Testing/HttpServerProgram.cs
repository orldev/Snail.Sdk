using System.Diagnostics;
using System.Globalization;
using System.Net;
using ModelContextProtocol.Client;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>A server under test over HTTP, started as a program on a free loopback port the way the platform starts it.</summary>
/// <remarks>
/// <para>
/// The twin of <see cref="ServerProgram"/> for a server one machine or one platform shares: the build beside the tests,
/// or the publication the override variable names — a native program among them — started with
/// <see cref="ServerProgram.Home"/> as its home and <see cref="ServerProgram.NoSettingsFile"/> as its settings file,
/// told its address through the variable it reads it from and told to answer over HTTP, and given back once it is ready.
/// Only the process shows what
/// a native build gets wrong, and only the process shows the refusal of a browser that the host it builds keeps.
/// </para>
/// <para>
/// A server that exits before it is ready fails the start at once with a <see cref="ServerExitedException"/> — its exit
/// code and what it wrote on stderr — rather than after the patience; a server disposed is killed with every process it
/// started, and waited for.
/// </para>
/// </remarks>
public sealed class HttpServerProgram : IAsyncDisposable
{
    /// <summary>The path a server answers with success once it is ready, unless the test names another.</summary>
    public const string DefaultReadiness = "/healthz";

    private const string McpPath = "mcp";

    private const int KeptLines = 10_000;

    private static readonly TimeSpan Attempt = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(100);

    private readonly Process _process;
    private readonly string _program;
    private readonly Queue<string> _log = new();
    private bool _isDisposed;

    private HttpServerProgram(Process process, string program, Uri address)
    {
        _process = process;
        _program = program;
        Address = address;

        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, line) => Keep(line.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <summary>Where the server listens: <c>http://127.0.0.1:&lt;port&gt;/</c>.</summary>
    public Uri Address { get; }

    /// <summary>The MCP endpoint of the server, <c>/mcp</c> under <see cref="Address"/>.</summary>
    public Uri Endpoint => new(Address, McpPath);

    /// <summary>What the server has written on stderr so far — its log — one line after another.</summary>
    /// <remarks>The last ten thousand lines: a test that runs a server long enough to write more asks about the latest.</remarks>
    public string Log
    {
        get
        {
            lock (_log)
                return string.Join('\n', _log);
        }
    }

    internal int ProcessId => _process.Id;

    /// <summary>
    /// The server started on a free loopback port, with these variables in its environment, and ready: its readiness
    /// path answered with success.
    /// </summary>
    /// <param name="assemblyName">The server's assembly, <c>Snail.MCP.&lt;Name&gt;</c>, as for <see cref="ServerProgram"/>.</param>
    /// <param name="overrideVariable">The variable that names a publication to start instead of the build beside the tests.</param>
    /// <param name="environment">
    /// Variables the server starts with beyond its home, its settings file and its address. A Snail server is given
    /// <c>SNAIL_MCP_&lt;SHORT&gt;_TRANSPORT=Http</c> as well unless these name that variable, so a server of both
    /// transports, which answers over stdio when its settings name no transport, starts over HTTP; its URL alone does
    /// not say HTTP, and the start waited for a server that was answering over stdio (Snail.MCP.Documents, 0.5.0).
    /// </param>
    /// <param name="addressVariable">
    /// The variable the server reads its address from; <c>SNAIL_MCP_&lt;SHORT&gt;_URL</c> unless named. It is given the URL,
    /// or the port alone when its name ends in <c>_PORT</c>.
    /// </param>
    /// <param name="readiness">
    /// The path that answers with success once the server is ready; null for a server that maps none, which is then ready
    /// when its MCP endpoint answers at all.
    /// </param>
    /// <param name="cancellationToken">Stops the wait; the server is killed.</param>
    /// <exception cref="ServerExitedException">The server exited before it was ready: its exit code and its stderr.</exception>
    /// <exception cref="InvalidOperationException">The server maps no readiness path.</exception>
    /// <exception cref="TimeoutException">The server was not ready within two minutes.</exception>
    public static Task<HttpServerProgram> StartAsync(
        string assemblyName,
        string overrideVariable,
        IDictionary<string, string?>? environment = null,
        string? addressVariable = null,
        string? readiness = DefaultReadiness,
        CancellationToken cancellationToken = default) =>
        StartAsync(assemblyName, overrideVariable, environment, addressVariable, readiness, ServerProgram.Patience, cancellationToken);

    /// <summary>The server started with a patience of the caller's own, for a test of the patience itself.</summary>
    internal static async Task<HttpServerProgram> StartAsync(
        string assemblyName,
        string overrideVariable,
        IDictionary<string, string?>? environment,
        string? addressVariable,
        string? readiness,
        TimeSpan patience,
        CancellationToken cancellationToken)
    {
        var variable = addressVariable ?? AddressVariableOf(assemblyName);
        var address = new Uri($"http://127.0.0.1:{Ports.Free()}/");
        var (command, arguments) = ServerProgram.Command(assemblyName, overrideVariable);
        var start = new ProcessStartInfo(command, arguments) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        var variables = ServerProgram.Variables(assemblyName, environment);

        variables[variable] = variable.EndsWith("_PORT", StringComparison.Ordinal) ? address.Port.ToString(CultureInfo.InvariantCulture) : address.ToString();

        if (TransportVariableOf(assemblyName) is { } transport && environment?.ContainsKey(transport) is not true)
            variables[transport] = nameof(McpTransport.Http);

        foreach (var (key, value) in variables)
            Set(start, key, value);

        var server = new HttpServerProgram(Process.Start(start)!, $"{command} {string.Join(' ', arguments)}".TrimEnd(), address);

        try
        {
            await server.WaitUntilReadyAsync(readiness, patience, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);

            throw;
        }

        return server;
    }

    /// <summary>A new session with the server, on its MCP endpoint with this query — <c>root=/workspaces/0c2b9e1f</c> — when one is given.</summary>
    public Task<McpClient> SessionAsync(string? query = null, McpClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        var endpoint = query is null ? Endpoint : new UriBuilder(Endpoint) { Query = query }.Uri;
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = endpoint });

        return McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken);
    }

    /// <summary>Kills the server with every process it started, and waits for it to end.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().ConfigureAwait(false);
        _process.Dispose();
    }

    /// <summary><c>Snail.MCP.Security</c> reads its address from <c>SNAIL_MCP_SECURITY_URL</c>, as the template's servers do.</summary>
    internal static string AddressVariableOf(string assemblyName) =>
        ServerProgram.ShortNameOf(assemblyName) is { } name
            ? $"SNAIL_MCP_{name.ToUpperInvariant()}_URL"
            : throw new ArgumentException($"'{assemblyName}' is not a Snail server's name: name the variable its address is read from.", nameof(assemblyName));

    /// <summary><c>Snail.MCP.Security</c> reads its transport from <c>SNAIL_MCP_SECURITY_TRANSPORT</c>; null for a program that is not a Snail server.</summary>
    internal static string? TransportVariableOf(string assemblyName) =>
        ServerProgram.ShortNameOf(assemblyName) is { } name ? $"SNAIL_MCP_{name.ToUpperInvariant()}_TRANSPORT" : null;

    private static void Set(ProcessStartInfo start, string key, string? value)
    {
        if (value is null)
            start.Environment.Remove(key);
        else
            start.Environment[key] = value;
    }

    private async Task WaitUntilReadyAsync(string? readiness, TimeSpan patience, CancellationToken cancellationToken)
    {
        var probe = readiness is null ? Endpoint : new Uri(Address, readiness);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        waiting.CancelAfter(patience);

        try
        {
            await PollAsync(probe, readiness is not null, waiting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{_program} did not answer GET {probe.AbsolutePath} within {patience.TotalSeconds:0} s{Said()}");
        }
    }

    private async Task PollAsync(Uri probe, bool isReadinessPath, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = Attempt };

        while (true)
        {
            if (_process.HasExited)
                throw await ExitedAsync(probe, cancellationToken).ConfigureAwait(false);

            var status = await StatusAsync(http, probe, cancellationToken).ConfigureAwait(false);

            if (isReadinessPath && status is HttpStatusCode.NotFound)
                throw new InvalidOperationException($"{_program} answered GET {probe.AbsolutePath} with 404: it maps no such path — map it, or start it with readiness: null to wait for its MCP endpoint");

            if (status is { } answered && (!isReadinessPath || (int)answered is >= 200 and < 300))
                return;

            await Task.Delay(Pause, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ServerExitedException> ExitedAsync(Uri probe, CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ServerExitedException(_program, _process.ExitCode, Log, $"GET {probe.AbsolutePath}");
    }

    private static async Task<HttpStatusCode?> StatusAsync(HttpClient http, Uri probe, CancellationToken cancellationToken)
    {
        try
        {
            using var answer = await http.GetAsync(probe, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            return answer.StatusCode;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private string Said() => Log is { Length: > 0 } log ? $"; its stderr:\n{log}" : "; it wrote nothing on stderr";

    private void Keep(string? line)
    {
        if (line is null)
            return;

        lock (_log)
        {
            _log.Enqueue(line);

            if (_log.Count > KeptLines)
                _log.Dequeue();
        }
    }
}
