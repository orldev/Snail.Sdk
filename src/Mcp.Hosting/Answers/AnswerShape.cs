namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>What a successful answer carries as its structured content beside the text.</summary>
public enum AnswerShape
{
    /// <summary>The data alone, without the envelope: what a client reads typed, and what most servers of the fleet send.</summary>
    Data,

    /// <summary>The whole envelope, <c>{"ok":true,"data":…}</c>, the same object as the text: for a server whose tools state it as their output schema.</summary>
    Envelope,

    /// <summary>No structured content: the text alone.</summary>
    None,
}
