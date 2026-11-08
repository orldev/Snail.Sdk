using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>How <see cref="SessionRegistration.WithSessionGrant"/> holds a session to its grant.</summary>
public sealed class GrantOptions
{
    /// <summary>Whether a tool the session may not call is left out of the list it is given; true unless set.</summary>
    /// <remarks>
    /// A model offered a tool it may not call spends a turn learning so. Listed, the tool still says what the server
    /// could do; a server that wants a withheld tool seen — to be told why rather than to invent a way round — sets
    /// this to false.
    /// </remarks>
    public bool HidesWithheldTools { get; set; } = true;

    /// <summary>Whether a session whose address names no tool is refused every tool, instead of being granted all of them.</summary>
    /// <remarks>
    /// For a server every role of a platform reaches, where a session opened without a grant is a mistake rather than a
    /// caller of its own. It applies only where there is an address (<see cref="ISessionAddress.IsPresent"/>): a server
    /// over stdio is a program for one caller, has nowhere to name a tool, and is never required to.
    /// </remarks>
    public bool RequiresGrant { get; set; }

    /// <summary>The type a refusal carries in the envelope: the one type of a call refused for want of a grant, whichever layer refuses it.</summary>
    /// <remarks>
    /// The door, a forwarder (<see cref="ToolForwarding.Refused(ModelContextProtocol.Server.RequestContext{CallToolRequestParams}, string)"/>), a catalog of commands (<see cref="ToolForwarding.Refused{T}(ModelContextProtocol.Server.RequestContext{CallToolRequestParams}, string)"/>)
    /// and a program's command alike: Snail.MCP.Git's catalog answered <c>notGranted</c> and its programs <c>refused</c>, and
    /// a program that branches on the type learned two words for one refusal.
    /// </remarks>
    public const string NotGranted = "notGranted";

    /// <summary>What a refused caller does next, unless <see cref="HintOf"/> says otherwise: the hint of every refusal of type <see cref="NotGranted"/> the SDK writes.</summary>
    /// <remarks>
    /// In the style of the hints beside it — <see cref="SessionRoots"/>, <c>run_tools</c>, <c>run_program</c>: lowercase,
    /// no closing stop — and without "session", which a stateless server has none of. Public so that a catalog that
    /// answers in its own shape can say the same; one that is given the request asks <see cref="ToolForwarding.Hint"/>,
    /// which answers the server's <see cref="HintOf"/> as well.
    /// </remarks>
    public const string Hint = "use only the tools that were granted; any other comes with a role the platform grants it to";

    /// <summary>What a caller refused <c>tool</c> does next, from the tool's name and the address the refused one was opened at; <see cref="Hint"/> unless set.</summary>
    /// <remarks>
    /// <para>
    /// One setting for every layer that refuses for want of a grant: the door's envelope, <see cref="ToolForwarding.Refused(ModelContextProtocol.Server.RequestContext{CallToolRequestParams}, string)"/>,
    /// <see cref="ToolForwarding.Refused{T}(ModelContextProtocol.Server.RequestContext{CallToolRequestParams}, string)"/> and a command of <c>run_program</c>. Before it, <see cref="Refusal"/> shaped the
    /// door and <see cref="ToolForwarding.Refused(ModelContextProtocol.Server.RequestContext{CallToolRequestParams}, string)"/> alone: Snail.MCP.Blender wrote its scope hint into a <see cref="Refusal"/>
    /// of its own and again into each forwarder, Snail.MCP.Git kept a hint of its own for its catalog, and a program's refused
    /// command said "this session was not granted it" — on a stateless server too, which has no session.
    /// </para>
    /// <para>
    /// The address says what the refused one is (<see cref="ISessionAddress.Subject"/>: <c>session</c> or <c>request</c>), for a
    /// hint that names it; outside any address it is <see cref="SessionAddress.None"/>. A <see cref="Refusal"/> set beside it
    /// still shapes the door's whole answer, and only <see cref="ToolForwarding.Hint"/> gives that shape this hint.
    /// </para>
    /// </remarks>
    public Func<string, ISessionAddress, string>? HintOf { get; set; }

    /// <summary>The answer a refused call gets, from the tool's name and the words of the refusal; the envelope of every answer, of type <see cref="NotGranted"/>, unless set.</summary>
    /// <remarks>
    /// Eight of nine fleet copies answered with the words alone; the first server written on 0.5.0 — the s3l-mcp template
    /// — wrote the same lambda to put them in the envelope its every other answer is, and so would every server after it.
    /// A server whose answers have another shape sets its own; one whose answers are the envelope and whose hint alone
    /// differs sets <see cref="HintOf"/> instead, which every layer reads — Snail.MCP.Blender set this one only to change the hint.
    /// </remarks>
    public Func<string, string, CallToolResult>? Refusal { get; set; }

    /// <summary>The hint of a refusal of <paramref name="tool"/> for the one served with <paramref name="services"/>: <see cref="HintOf"/>, <see cref="Hint"/> unless set.</summary>
    internal string HintFor(string tool, IServiceProvider? services) =>
        HintOf?.Invoke(tool, services?.GetService<ISessionAddress>() ?? SessionAddress.None) ?? Hint;

    /// <summary>The refusal of a call to <paramref name="tool"/>, shaped as the server asked.</summary>
    internal CallToolResult Refused(string tool, string words, IServiceProvider? services) =>
        Refusal?.Invoke(tool, words) ?? Envelope(words, HintFor(tool, services));

    /// <summary>The words in the envelope with the hint, marked an error.</summary>
    internal static CallToolResult Envelope(string words, string hint) =>
        ToolAnswer.Failed(words, hint, NotGranted);
}
