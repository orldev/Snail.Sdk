using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Snail.Sdk.Docs;

/// <summary>A tool as the server's host holds it: its wire object, as the server's own MCP SDK writes it, and the skill it belongs to.</summary>
internal sealed record HostTool(JsonElement Wire, string? Skill);

/// <summary>Every tool the server's host registered, or why the host could not be read.</summary>
internal sealed record HostReading(IReadOnlyList<HostTool> Tools, string? Failure)
{
    public static HostReading Failed(string why) => new([], why);
}

/// <summary>The tools a server's host holds, the ones Snail.Sdk registers included, as a client is sent them.</summary>
/// <remarks>
/// <para>
/// <c>find_tool</c>, <c>run_tools</c>, <c>run_program</c> and the skill tools are no methods of a class of the server: its
/// <c>With…</c> calls register them, named and described from the options it passes — the short name in the skill tools,
/// the limits in <c>run_program</c>'s description, <c>enable</c> in <c>find_tool</c>'s schema once skills are on. Only
/// the server's composition says which it enables and how, and no metadata written at build time could follow a lambda
/// that sets an option. Asking a running server over stdio would miss an HTTP-only one and the tools of skills, which a
/// session lists only once loaded.
/// </para>
/// <para>
/// So the server's entry point runs until its host is built — the moment Microsoft.Extensions.Hosting announces on
/// its DiagnosticListener — and is stopped there with HostAbortedException, as <c>dotnet ef</c> and ASP.NET Core's
/// build-time OpenAPI document stop an application. The tools are read from <c>McpServerOptions.ToolCollection</c>, the
/// collection the protocol serves. No hosted service starts, no transport opens, no tool runs. What runs is the code
/// before <c>Build()</c>: reading the settings, composing the services — and a registration may create its data folder
/// or open its database there.
/// </para>
/// <para>
/// That code runs in a process of its own, <c>Snail.Sdk.Docs host</c>, on a machine that is not the developer's: an
/// empty temporary home (<c>HOME</c> and <c>USERPROFILE</c>, <c>APPDATA</c> and <c>LOCALAPPDATA</c> on Windows) as its
/// home and its working directory, no <c>XDG_*_HOME</c>, none of the server's <c>SNAIL_MCP_&lt;SHORT&gt;_</c> variables but
/// <c>SNAIL_MCP_&lt;SHORT&gt;_CONFIG</c> naming a file that never exists — the only settings file the server then reads,
/// so it reads none — and no <c>DOTNET_RUNNING_IN_CONTAINER</c>, the same machine everywhere; the home is deleted with
/// whatever the server wrote into it. Snail.Sdk.Mcp.Testing's <c>ServerProgram</c> starts a server under test the same way.
/// A process rather than variables set around the call in this one: on Unix <c>Environment.SetEnvironmentVariable</c>
/// changes .NET's copy alone, which native code never reads; a thread or a timer the composition started outlives the
/// stop, and would see the real home once the variables were put back; and an entry point that never builds its host
/// can be ended, where a thread cannot. On macOS <c>ApplicationData</c> and <c>LocalApplicationData</c> come from the
/// system and not from <c>HOME</c>, and Windows' known folders follow no variable: a server's folders under
/// <c>ServerFolders</c>, read from the home, are the ones the sandbox moves.
/// </para>
/// </remarks>
internal static class HostTools
{
    /// <summary>The command of this program that reads a server's host in the sandbox and writes what it read into a file.</summary>
    public const string Command = "host";

    private const string Hosting = "Microsoft.Extensions.Hosting";

    private const string NameKey = "Snail.Sdk.Mcp.ServerName";

    private const string ServerPrefix = "Snail.MCP.";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>The entry point's patience and the time a process takes to start and load the server.</summary>
    private static readonly TimeSpan ProcessPatience = Patience + TimeSpan.FromSeconds(30);

    private static readonly string[] Unset = ["XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_STATE_HOME", "XDG_RUNTIME_DIR", "DOTNET_RUNNING_IN_CONTAINER"];

    /// <summary>Reads the server's host in a process of its own with a home of its own; a failure says why instead of throwing.</summary>
    public static HostReading Read(ServerContext context)
    {
        if (context.Server.EntryPoint is null)
            return HostReading.Failed("the server's assembly has no entry point");

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), SandboxName())).FullName;

        try
        {
            return ReadIn(root, context.Server.Location, ShortNameOf(context.Server));
        }
        finally
        {
            Remove(root);
        }
    }

    /// <summary>The reading process, started in the sandbox under the root and awaited, and what it wrote.</summary>
    private static HostReading ReadIn(string root, string server, string? name)
    {
        var home = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
        var answer = Path.Combine(root, "tools.json");
        var start = Sandboxed(home, Path.Combine(root, "no-settings-file.json"), name);

        start.ArgumentList.Add(Command);
        start.ArgumentList.Add(server);
        start.ArgumentList.Add(answer);

        using var reader = Process.Start(start) ?? throw new InvalidOperationException($"{start.FileName} did not start");
        var said = new Queue<string>();

        reader.OutputDataReceived += (_, _) => { };
        reader.ErrorDataReceived += (_, line) => Keep(said, line.Data);
        reader.BeginOutputReadLine();
        reader.BeginErrorReadLine();

        if (!reader.WaitForExit(ProcessPatience))
        {
            reader.Kill(entireProcessTree: true);
            reader.WaitForExit();

            return File.Exists(answer) ? Answered(answer) : HostReading.Failed($"its entry point built no host within {Patience.TotalSeconds} seconds");
        }

        reader.WaitForExit();

        if (File.Exists(answer))
            return Answered(answer);

        lock (said)
            return HostReading.Failed($"the process reading its host ended with exit code {reader.ExitCode} before answering: {string.Join(" ", said)}");
    }

    /// <summary>
    /// How this program starts itself on the sandbox: its home, its working directory, its settings file that never exists,
    /// and none of the developer's variables that would lead the server back to their machine.
    /// </summary>
    private static ProcessStartInfo Sandboxed(string home, string noSettingsFile, string? name)
    {
        var (command, arguments) = Itself();
        var start = new ProcessStartInfo(command) { WorkingDirectory = home, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["HOME"] = home;
        start.Environment["USERPROFILE"] = home;

        if (OperatingSystem.IsWindows())
        {
            start.Environment["APPDATA"] = Path.Combine(home, "AppData", "Roaming");
            start.Environment["LOCALAPPDATA"] = Path.Combine(home, "AppData", "Local");
        }

        foreach (var variable in Unset)
            start.Environment.Remove(variable);

        if (name is null)
            return start;

        var prefix = $"SNAIL_MCP_{name.ToUpperInvariant()}_";
        var own = start.Environment.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var variable in own)
            start.Environment.Remove(variable);

        start.Environment[$"{prefix}CONFIG"] = noSettingsFile;

        return start;
    }

    /// <summary>This program: its apphost, or its dll through the dotnet that runs it.</summary>
    private static (string Command, string[] Arguments) Itself()
    {
        var self = typeof(HostTools).Assembly.Location;
        var process = Environment.ProcessPath;
        var running = process is null ? null : Path.GetFileNameWithoutExtension(process);

        if (running == Path.GetFileNameWithoutExtension(self))
            return (process!, []);

        if (running == "dotnet")
            return (process!, [self]);

        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        return (string.IsNullOrEmpty(host) ? "dotnet" : host, [self]);
    }

    /// <summary>The server's short name: from the command Snail.Sdk.Mcp writes into its assembly, else from <c>Snail.MCP.&lt;Name&gt;</c>.</summary>
    internal static string? ShortNameOf(Assembly server)
    {
        var command = server.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == typeof(AssemblyMetadataAttribute).FullName && attribute.ConstructorArguments.Count == 2)
            .Where(attribute => attribute.ConstructorArguments[0].Value as string == NameKey)
            .Select(attribute => attribute.ConstructorArguments[1].Value as string)
            .FirstOrDefault();

        if (command is { Length: > 8 } && command.StartsWith("s3l-mcp-", StringComparison.Ordinal))
            return command[8..];

        var assembly = server.GetName().Name ?? string.Empty;

        return assembly.StartsWith(ServerPrefix, StringComparison.OrdinalIgnoreCase) && assembly.Length > ServerPrefix.Length
            ? assembly[ServerPrefix.Length..].Replace(".", string.Empty, StringComparison.Ordinal).ToLowerInvariant()
            : null;
    }

    /// <summary>The last lines the reading process wrote to stderr, for the warning when it answered nothing.</summary>
    private static void Keep(Queue<string> said, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (said)
        {
            said.Enqueue(line.Trim());

            if (said.Count > 5)
                said.Dequeue();
        }
    }

    /// <summary>What the reading process wrote: the tools as wire objects with their skills, or why there are none.</summary>
    private static HostReading Answered(string answer)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(answer));
        var root = document.RootElement;

        if (root.TryGetProperty("failure", out var failure) && failure.ValueKind == JsonValueKind.String)
            return HostReading.Failed(failure.GetString()!);

        var tools = root.GetProperty("tools").EnumerateArray()
            .Select(tool => new HostTool(tool.GetProperty("wire").Clone(), tool.TryGetProperty("skill", out var skill) ? skill.GetString() : null))
            .ToList();

        return new HostReading(tools, null);
    }

    /// <summary>
    /// The sandbox's folder, named as the standard names a test's temporary folder — <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>
    /// — so that a cleaner proves one a killed build left behind a leftover.
    /// </summary>
    private static string SandboxName()
    {
        using var process = Process.GetCurrentProcess();
        var start = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();

        return $"s3l-test-{Environment.ProcessId}-{start}-{Path.GetRandomFileName()[..8]}";
    }

    /// <summary>Deletes the sandbox with whatever the server wrote into it; one that cannot be deleted is left to the system's temporary folder.</summary>
    private static void Remove(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception kept) when (kept is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Snail.Sdk.Docs: the server's temporary home {root} was not deleted: {kept.Message}");
        }
    }

    /// <summary>
    /// <c>Snail.Sdk.Docs host</c>, inside the sandbox: loads the server, runs its entry point until its host is built, writes
    /// the tools or why there are none into the answer, and ends the process — a thread the server's composition started
    /// would keep it alive.
    /// </summary>
    [DoesNotReturn]
    public static void Answer(string assemblyPath, string answerPath)
    {
        var written = new JsonObject();

        try
        {
            var context = ServerContext.Open(assemblyPath);
            var reading = ReadHere(context);

            written["failure"] = reading.Failure;
            written["tools"] = new JsonArray([.. reading.Tools.Select(tool => new JsonObject { ["wire"] = JsonNode.Parse(tool.Wire.GetRawText()), ["skill"] = tool.Skill })]);
        }
        catch (Exception unreadable)
        {
            written["failure"] = $"the server could not be loaded to read its host: {unreadable.GetType().Name}: {unreadable.Message}";
        }

        File.WriteAllText(answerPath, written.ToJsonString());
        Environment.Exit(0);
    }

    /// <summary>Runs the server's entry point in this process until its host is built and reads its tools; a failure says why instead of throwing.</summary>
    /// <remarks>The server is its own entry assembly meanwhile: WithSkills and McpHttpHost read its short name from there.</remarks>
    private static HostReading ReadHere(ServerContext context)
    {
        if (context.Server.EntryPoint is not { } entry)
            return HostReading.Failed("the server's assembly has no entry point");

        var reading = new Outcome();
        var previous = Assembly.GetEntryAssembly();

        using var subscription = DiagnosticListener.AllListeners.Subscribe(new Listeners(context, reading));

        Assembly.SetEntryAssembly(context.Server);

        try
        {
            var thread = new Thread(() => Run(entry, reading)) { IsBackground = true, Name = "Snail.Sdk.Docs host" };

            thread.Start();

            return reading.Within(Patience) ?? HostReading.Failed($"its entry point built no host within {Patience.TotalSeconds} seconds");
        }
        finally
        {
            Assembly.SetEntryAssembly(previous);
        }
    }

    /// <summary>The entry point, run to the host it builds; whatever ends it before that is the reason there are no tools.</summary>
    private static void Run(MethodInfo entry, Outcome reading)
    {
        try
        {
            entry.Invoke(null, entry.GetParameters().Length == 0 ? [] : [Array.Empty<string>()]);
            reading.Set(HostReading.Failed("its entry point ended without building a host"));
        }
        catch (Exception failed)
        {
            var cause = failed is TargetInvocationException { InnerException: { } inner } ? inner : failed;

            reading.Set(HostReading.Failed($"its entry point failed before its host was built: {cause.GetType().Name}: {cause.Message}"));
        }
    }

    /// <summary>The tools of a built host: its MCP server options' collection, in the order the server registered them.</summary>
    private static IReadOnlyList<HostTool> Tools(ServerContext context, object host)
    {
        var contract = host.GetType().GetInterfaces().FirstOrDefault(type => type.FullName == $"{Hosting}.IHost")
            ?? throw new InvalidOperationException($"{host.GetType().Name} is no IHost");
        var services = (IServiceProvider)contract.GetProperty("Services")!.GetValue(host)!;
        var optionsType = context.SdkType("ModelContextProtocol.Server.McpServerOptions")
            ?? throw new InvalidOperationException("the server carries no ModelContextProtocol.Core");
        var wrapper = LoadedType(context, "Microsoft.Extensions.Options", "Microsoft.Extensions.Options.IOptions`1").MakeGenericType(optionsType);
        var options = wrapper.GetProperty("Value")!.GetValue(services.GetService(wrapper)
            ?? throw new InvalidOperationException("the host registers no MCP server (AddMcpServer)"));
        var collection = optionsType.GetProperty("ToolCollection")!.GetValue(options) as IEnumerable;

        return collection is null ? [] : [.. collection.Cast<object>().Select(tool => Tool(context, tool))];
    }

    private static HostTool Tool(ServerContext context, object tool)
    {
        var type = tool.GetType();
        var protocol = type.GetProperty("ProtocolTool")!.GetValue(tool)!;
        var metadata = type.GetProperty("Metadata")?.GetValue(tool) as IEnumerable ?? Array.Empty<object>();
        var skill = metadata.Cast<object>()
            .Where(attribute => attribute.GetType().Name == "SkillAttribute")
            .Select(attribute => attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string)
            .FirstOrDefault();

        return new HostTool(ToolCatalog.Wire(context, protocol), skill);
    }

    private static Type LoadedType(ServerContext context, string assembly, string fullName)
    {
        var loaded = context.Assemblies.FirstOrDefault(candidate => candidate.GetName().Name == assembly)
            ?? context.LoadFromAssemblyName(new AssemblyName(assembly));

        return loaded.GetType(fullName, throwOnError: true)!;
    }

    /// <summary>The exception the host's own builder stops with, so that the server's code treats the stop as one and not as a crash.</summary>
    private static Exception Abort(object host)
    {
        const string message = "Snail.Sdk.Docs read the server's tools and stops it before it starts.";
        var contract = host.GetType().GetInterfaces().FirstOrDefault(type => type.FullName == $"{Hosting}.IHost");
        var aborted = contract?.Assembly.GetType($"{Hosting}.HostAbortedException");

        return aborted is null ? new OperationCanceledException(message) : (Exception)Activator.CreateInstance(aborted, message)!;
    }

    /// <summary>Waits for the hosting listener among every DiagnosticListener the process makes.</summary>
    private sealed class Listeners(ServerContext context, Outcome reading) : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == Hosting)
                value.Subscribe(new Events(context, reading));
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    /// <summary>On HostBuilt, reads the tools and stops the entry point before the host runs.</summary>
    private sealed class Events(ServerContext context, Outcome reading) : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != "HostBuilt" || value.Value is not { } host || reading.IsSet)
                return;

            try
            {
                reading.Set(new HostReading(Tools(context, host), null));
            }
            catch (Exception unreadable)
            {
                var cause = unreadable is TargetInvocationException { InnerException: { } inner } ? inner : unreadable;

                reading.Set(HostReading.Failed($"its tools could not be read: {cause.GetType().Name}: {cause.Message}"));
            }

            throw Abort(host);
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    /// <summary>The first reading set, from the thread of the entry point, awaited on the generator's own without a task to block on.</summary>
    private sealed class Outcome
    {
        private readonly object _gate = new();
        private HostReading? _reading;

        public bool IsSet
        {
            get
            {
                lock (_gate)
                    return _reading is not null;
            }
        }

        public void Set(HostReading reading)
        {
            lock (_gate)
            {
                _reading ??= reading;
                Monitor.PulseAll(_gate);
            }
        }

        public HostReading? Within(TimeSpan patience)
        {
            lock (_gate)
            {
                if (_reading is null)
                    Monitor.Wait(_gate, patience);

                return _reading;
            }
        }
    }
}
