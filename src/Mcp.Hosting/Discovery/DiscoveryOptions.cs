namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>How <c>find_tool</c> reads a server's tools: the words they are known by, the words that say nothing, and what a program calls them.</summary>
public sealed class DiscoveryOptions
{
    /// <summary>The prefix every tool of the server shares, such as <c>git_</c>; its words are dropped from names and tasks alike.</summary>
    /// <remarks>A word every tool's name carries ranks nothing and matches everything: "git status" found every git_ tool before git_status.</remarks>
    public string? Prefix { get; set; }

    /// <summary>The words each tool is asked for by beyond its name, by the tool's name: <c>["git_push"] = "publish send upstream share"</c>.</summary>
    /// <remarks>
    /// A word here weighs as one in the name. The words live here rather than in the descriptions because a description is
    /// read by every session whether or not it searches, and these only when one does.
    /// </remarks>
    public IDictionary<string, string> Meanings { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Words dropped from every task besides the common English ones, such as <c>bpy</c> or <c>ae</c>.</summary>
    public ISet<string> Noise { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a sequence or a program of the server calls a tool, by the tool's name; null for a tool that is no command.</summary>
    /// <remarks>Answered beside each tool as <c>command</c>, so a model that found a tool can send it through <c>run_program</c> at once.</remarks>
    public Func<string, string?>? CommandOf { get; set; }

    /// <summary>The tools searched, instead of the tools the server registered (<see cref="ToolCatalog"/>).</summary>
    /// <remarks>For a server whose tools are not all in its tool collection. Left unset, the catalog is what the server registered, skills included.</remarks>
    public IReadOnlyList<ToolEntry>? Catalog { get; set; }
}
