using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>Marks a tool that calls other tools of its server — <c>run_tools</c>, <c>run_program</c> — so that no forwarder runs it.</summary>
/// <remarks>
/// Read from <see cref="McpServerTool.Metadata"/>, where the MCP SDK puts the attributes of a tool's method when it builds
/// the tool: no lookup at run time, so a native build reads it as the JIT does.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ForwardsAttribute : Attribute;

/// <summary>The one way a tool calls another tool of its server: held to the session's grant, and never one forwarder inside another.</summary>
/// <remarks>
/// <para>
/// A tool that invokes another tool's object runs past the door (<see cref="SessionRegistration.WithSessionGrant"/>),
/// which sees only what the protocol delivers. Three servers of the fleet let a role granted <c>run_tools</c> call every
/// tool they had, the withheld ones included, until each checked the grant again in its forwarder — and each wrote that
/// check itself. Here it is written once: a forwarder that calls its steps through <see cref="CallAsync"/> cannot forget it.
/// </para>
/// <para>
/// A forwarder run by a forwarder is refused: twenty steps of twenty steps is four hundred calls from one. A tool marked
/// <see cref="ForwardsAttribute"/> is refused as a step; a step that forwards without the mark is refused at its first call.
/// </para>
/// </remarks>
public static class ToolForwarding
{
    /// <summary>The step being forwarded on this flow of work, so that a step that forwards in turn is caught unmarked.</summary>
    private static readonly AsyncLocal<string?> Forwarded = new();

    /// <summary>
    /// Runs <paramref name="tool"/> as the protocol would — the same request, its services and session, the arguments bound
    /// by the tool's own schema — when the session may call it; the refusal, in the server's shape, otherwise.
    /// </summary>
    /// <param name="context">The request of the forwarder: the call the client made.</param>
    /// <param name="tool">The step, one of the server's tools.</param>
    /// <param name="arguments">The step's arguments, as the client would have sent them.</param>
    /// <param name="cancellationToken">Ends the step.</param>
    public static async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> context, McpServerTool tool, JsonObject? arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tool);

        var name = tool.ProtocolTool.Name;
        var refusal = Nested(context, tool) ?? Refusal(context, name);

        if (refusal is not null)
            return Shaped(SessionRegistration.ServicesOf(context), name, refusal);

        Forwarded.Value = name;

        return await tool.InvokeAsync(Step(context, tool, arguments), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Why the session of <paramref name="context"/> may not call <paramref name="tool"/>, or null when it may.</summary>
    /// <remarks>
    /// For a forwarder whose steps are not tools — commands of a catalog, each granted under the name of the tool it
    /// stands for (Snail.MCP.Git: <c>status</c> under <c>git_status</c>). Null as well when the server holds no grant.
    /// The words alone; <see cref="Refused(RequestContext{CallToolRequestParams}, string)"/> and
    /// <see cref="Refused{T}(RequestContext{CallToolRequestParams}, string)"/> are the refusal as an answer.
    /// </remarks>
    public static string? Refusal(RequestContext<CallToolRequestParams> context, string tool)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        return SessionRegistration.ServicesOf(context) is { } services ? Refusal(services, tool) : null;
    }

    /// <summary>Why the one served with <paramref name="services"/> may not call <paramref name="tool"/>, or null when it may.</summary>
    /// <remarks>
    /// The same question without a request: for a forwarder handed its services rather than its request — a job, a batch
    /// in a second process — and for a unit test that has a container and no protocol. Snail.MCP.Blender asked
    /// <see cref="IToolGate"/> itself for its batches, since the request overload was all there was.
    /// </remarks>
    /// <param name="services">The services of the request — or of the session, where requests share them.</param>
    /// <param name="tool">The tool's name, or the command's where it stands for no tool.</param>
    public static string? Refusal(IServiceProvider services, string tool)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(tool);

        return services.GetService<IToolGate>()?.Refusal(tool, services);
    }

    /// <summary>The answer a call to <paramref name="tool"/> gets when the session of <paramref name="context"/> may not make it — the door's, in the server's shape — or null when it may.</summary>
    /// <remarks>
    /// For a forwarder whose steps are commands rather than tools and whose answers are the protocol's: the refusal reads
    /// as the door's does, type <see cref="GrantOptions.NotGranted"/> with the server's hint (<see cref="GrantOptions.HintOf"/>),
    /// or as the server's <see cref="GrantOptions.Refusal"/> shapes it — no copy of either to keep.
    /// </remarks>
    public static CallToolResult? Refused(RequestContext<CallToolRequestParams> context, string tool) =>
        Refusal(context, tool) is { } words ? Shaped(SessionRegistration.ServicesOf(context), tool, words) : null;

    /// <summary>The answer a call to <paramref name="tool"/> gets when the one served with <paramref name="services"/> may not make it, or null when it may.</summary>
    /// <remarks>The overload of <see cref="Refused(RequestContext{CallToolRequestParams}, string)"/> without a request, as <see cref="Refusal(IServiceProvider, string)"/> is.</remarks>
    public static CallToolResult? Refused(IServiceProvider services, string tool) =>
        Refusal(services, tool) is { } words ? Shaped(services, tool, words) : null;

    /// <summary>The failed answer a command standing for <paramref name="tool"/> gets when the session of <paramref name="context"/> may not call it, or null when it may.</summary>
    /// <remarks>
    /// For a catalog whose commands answer <see cref="Answer{T}"/> before a tool makes the envelope of it: the gate's words,
    /// the server's hint (<see cref="Hint"/>) and type <see cref="GrantOptions.NotGranted"/> — the type the door gives, so that
    /// a program branching on it meets one word for one refusal. Snail.MCP.Git wrote the type and a hint of its own.
    /// </remarks>
    public static Answer<T>? Refused<T>(RequestContext<CallToolRequestParams> context, string tool) =>
        Refusal(context, tool) is { } words ? Answer.Failed<T>(words, HintOf(SessionRegistration.ServicesOf(context), tool), GrantOptions.NotGranted) : null;

    /// <summary>The failed answer a command standing for <paramref name="tool"/> gets when the one served with <paramref name="services"/> may not call it, or null when it may.</summary>
    /// <remarks>The overload of <see cref="Refused{T}(RequestContext{CallToolRequestParams}, string)"/> without a request.</remarks>
    public static Answer<T>? Refused<T>(IServiceProvider services, string tool) =>
        Refusal(services, tool) is { } words ? Answer.Failed<T>(words, HintOf(services, tool), GrantOptions.NotGranted) : null;

    /// <summary>What a caller refused <paramref name="tool"/> does next, for the one served with <paramref name="services"/>: the server's <see cref="GrantOptions.HintOf"/>, <see cref="GrantOptions.Hint"/> unless set.</summary>
    /// <remarks>
    /// The hint every refusal of type <see cref="GrantOptions.NotGranted"/> carries — the door's, a forwarder's, a program's
    /// command's. For an answer of the server's own shape: a <see cref="GrantOptions.Refusal"/>, a catalog that answers
    /// neither <see cref="CallToolResult"/> nor <see cref="Answer{T}"/>.
    /// </remarks>
    public static string Hint(IServiceProvider services, string tool)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tool);

        return HintOf(services, tool);
    }

    /// <summary>Whether the tool calls other tools itself: it carries <see cref="ForwardsAttribute"/>.</summary>
    public static bool IsForwarder(McpServerTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool.Metadata.OfType<ForwardsAttribute>().Any();
    }

    /// <summary>Why the step would be one forwarder inside another, or null when it would not.</summary>
    private static string? Nested(RequestContext<CallToolRequestParams> context, McpServerTool tool)
    {
        var caller = context.Params?.Name ?? "a forwarder";

        if (Forwarded.Value is { } outer)
            return $"'{outer}' was itself run by a forwarder, and a step runs no other tool";

        return IsForwarder(tool)
            ? $"'{tool.ProtocolTool.Name}' runs other tools itself, so '{caller}' does not run it"
            : null;
    }

    /// <summary>The refusal in the shape the server asked for at the door, the envelope when it asked for none.</summary>
    private static CallToolResult Shaped(IServiceProvider? services, string tool, string words) =>
        services?.GetService<GrantOptions>()?.Refused(tool, words, services) ?? GrantOptions.Envelope(words, GrantOptions.Hint);

    /// <summary>The server's hint for a refusal of <paramref name="tool"/>, the fleet's where the server holds no grant.</summary>
    private static string HintOf(IServiceProvider? services, string tool) =>
        services?.GetService<GrantOptions>()?.HintFor(tool, services) ?? GrantOptions.Hint;

    /// <summary>The request of the step: the forwarder's, naming the step and its arguments, with the step as the matched tool.</summary>
    private static RequestContext<CallToolRequestParams> Step(RequestContext<CallToolRequestParams> context, McpServerTool tool, JsonObject? arguments)
    {
        var parameters = new CallToolRequestParams { Name = tool.ProtocolTool.Name, Arguments = Given(arguments) };

        return new RequestContext<CallToolRequestParams>(context.Server, context.JsonRpcRequest, parameters)
        {
            MatchedPrimitive = tool,
            Services = context.Services,
            User = context.User,
            Items = context.Items,
        };
    }

    /// <summary>The arguments as the protocol carries them, read once from their JSON.</summary>
    private static Dictionary<string, JsonElement>? Given(JsonObject? arguments)
    {
        if (arguments is null)
            return null;

        var element = JsonElement.Parse(arguments.ToJsonString());

        return element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
    }
}
