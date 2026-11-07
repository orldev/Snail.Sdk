using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>How <see cref="SessionRegistration.RefuseWhatWasNotGranted"/> holds a session to its grant.</summary>
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

    /// <summary>The type a refusal carries in the envelope.</summary>
    public const string NotGranted = "notGranted";

    /// <summary>The answer a refused call gets, from the tool's name and the words of the refusal; the envelope of every answer, of type <see cref="NotGranted"/>, unless set.</summary>
    /// <remarks>
    /// Eight of nine fleet copies answered with the words alone; the first server written on 0.5.0 — the s3l-mcp template
    /// — wrote the same lambda to put them in the envelope its every other answer is, and so would every server after it.
    /// A server whose answers have another shape sets its own.
    /// </remarks>
    public Func<string, string, CallToolResult>? Refusal { get; set; }

    /// <summary>The refusal of a call to <paramref name="tool"/>, shaped as the server asked.</summary>
    internal CallToolResult Refused(string tool, string words) =>
        Refusal?.Invoke(tool, words) ?? Envelope(words);

    /// <summary>The words in the envelope, marked an error.</summary>
    internal static CallToolResult Envelope(string words) =>
        ToolAnswer.Failed(words, GrantHint, NotGranted);

    private const string GrantHint = "Use a tool this session was granted; a tool beyond them comes with a role the platform grants it to.";
}
