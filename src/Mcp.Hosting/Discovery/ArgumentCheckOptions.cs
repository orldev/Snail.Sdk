namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>What the argument check offers for a parameter a call names wrongly, beyond the names spelled like it.</summary>
public sealed class ArgumentCheckOptions
{
    /// <summary>The parameters a name may stand for, by the name a call sends: <c>["query"] = ["task", "intent"]</c>.</summary>
    /// <remarks>
    /// <para>
    /// A wrong name is offered the parameters spelled like it or containing it; a synonym is spelled like nothing it means.
    /// Snail.MCP.Blender's logs held <c>query</c> sent for <c>intent</c> and <c>program</c> for <c>code</c>, and the check
    /// guessed nothing for either. A refusal of a tool that takes one of the names offers it, the first two in their order;
    /// the names are matched ignoring case.
    /// </para>
    /// <para><c>find_tool</c> takes <c>query</c> for <c>task</c> itself, as it takes <c>intent</c>.</para>
    /// </remarks>
    public IDictionary<string, IReadOnlyList<string>> Synonyms { get; set; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
