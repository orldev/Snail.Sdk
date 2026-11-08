using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace Snail.Sdk.Docs;

/// <summary>A tool as the server's host holds it: its wire object, not yet serialized, and the skill it belongs to.</summary>
internal sealed record HostTool(object Protocol, string? Skill);

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
/// So the server's entry point runs here until its host is built — the moment Microsoft.Extensions.Hosting announces on
/// its DiagnosticListener — and is stopped there with HostAbortedException, as <c>dotnet ef</c> and ASP.NET Core's
/// build-time OpenAPI document stop an application. The tools are read from <c>McpServerOptions.ToolCollection</c>, the
/// collection the protocol serves. No hosted service starts, no transport opens, no tool runs. What runs is the code
/// before <c>Build()</c>: reading the settings, composing the services.
/// </para>
/// </remarks>
internal static class HostTools
{
    private const string Hosting = "Microsoft.Extensions.Hosting";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>Runs the server's entry point until its host is built and reads its tools; a failure says why instead of throwing.</summary>
    /// <remarks>The server is its own entry assembly meanwhile: WithSkills and McpHttpHost read its short name from there.</remarks>
    public static HostReading Read(ServerContext context)
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

        return collection is null ? [] : [.. collection.Cast<object>().Select(Tool)];
    }

    private static HostTool Tool(object tool)
    {
        var type = tool.GetType();
        var protocol = type.GetProperty("ProtocolTool")!.GetValue(tool)!;
        var metadata = type.GetProperty("Metadata")?.GetValue(tool) as IEnumerable ?? Array.Empty<object>();
        var skill = metadata.Cast<object>()
            .Where(attribute => attribute.GetType().Name == "SkillAttribute")
            .Select(attribute => attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string)
            .FirstOrDefault();

        return new HostTool(protocol, skill);
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
