using ModelContextProtocol.Protocol;

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

    /// <summary>The answer a refused call gets, from the tool's name and the words of the refusal; one text block with the words, marked an error, unless set.</summary>
    /// <remarks>The default is the shape eight of nine fleet copies answered with; a server whose every answer is an envelope of its own shapes its refusal the same way.</remarks>
    public Func<string, string, CallToolResult>? Refusal { get; set; }

    /// <summary>The refusal of a call to <paramref name="tool"/>, shaped as the server asked.</summary>
    internal CallToolResult Refused(string tool, string words) =>
        Refusal?.Invoke(tool, words) ?? Plain(words);

    /// <summary>One text block with the words, marked an error.</summary>
    internal static CallToolResult Plain(string words) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = words }] };
}
