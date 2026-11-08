using Snail.Sdk.Mcp.Hosting.Answers;

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

    /// <summary>Where the command of a tool takes one of the tool's parameters or one of their fields, by the tool's name and the dotted path in the input schema's names; null drops it.</summary>
    /// <remarks>
    /// <para>
    /// Applied to every tool that has a command (<see cref="CommandOf"/>), so <c>find_tool</c> names the parameters as a
    /// program sends them. A bridge's commands took <c>light_type</c> where the tool took <c>lightType</c>, and a tool's
    /// own <c>timeoutSeconds</c> went to no command at all: a program written from the answer sent <c>lightType</c>, which
    /// the command ignored without a word (Snail.MCP.Blender, AfterEffects).
    /// </para>
    /// <para>
    /// A parameter is asked by its name, <c>("blender_set_camera_optics", "focus")</c>; a field of an object parameter by
    /// its path from the parameter, <c>"focus.distance"</c>, <c>"output.fileFormat"</c>, two levels down at most
    /// (<see cref="ToolParameter.Fields"/>). The answer says where the command takes it:
    /// </para>
    /// <list type="bullet">
    /// <item><description>a name without a dot, <c>"file_format"</c>, puts it at the top level of the command: a parameter
    /// under that name, and a field lifted out of its object, for a server that sends the object's fields beside the
    /// tool's other parameters;</description></item>
    /// <item><description>a dotted path, <c>"focus.distance"</c>, keeps a field inside its parent, named after the last
    /// dot; where the parent goes is the parent's own answer;</description></item>
    /// <item><description>null drops it, and the fields kept inside it with it.</description></item>
    /// </list>
    /// <para>
    /// A parameter dropped while its fields are lifted disappears, and they take its place. Snail.MCP.Blender's
    /// <c>blender_render_job</c> takes <c>output</c> and <c>queue</c> as records and sends their fields at the top of the
    /// <c>render_job</c> command — <c>file_format</c>, <c>samples</c>, <c>chunks</c>, <c>retries</c> — so it answers null
    /// for <c>output</c> and <c>"file_format"</c> for <c>output.fileFormat</c>; <c>blender_set_camera_optics</c> sends
    /// <c>focus</c> whole, so it answers <c>"focus"</c> and <c>"focus.distance"</c>. Before fields were asked, both records
    /// went out of the answer, or came as an <c>object</c> with nothing in it, and a program rendered with the defaults or
    /// guessed the fields.
    /// </para>
    /// <para>
    /// A field lifted is required only when it and every parameter above it are. A field of the items of an array stays in
    /// each item, whatever its name. A function that knows only the command's top-level names, and answers null for any
    /// other, drops every field — the answer of before; one that hands the path back unchanged keeps the fields where the
    /// schema has them.
    /// </para>
    /// <para>A tool without a command keeps the names and the nesting of its input schema, which is how a direct call or a step sends them.</para>
    /// </remarks>
    public Func<string, string, string?>? ParameterOf { get; set; }

    /// <summary>The tools searched, instead of the tools the server registered (<see cref="ToolCatalog"/>).</summary>
    /// <remarks>For a server whose tools are not all in its tool collection. Left unset, the catalog is what the server registered, skills included.</remarks>
    public IReadOnlyList<ToolEntry>? Catalog { get; set; }

    /// <summary>Tasks in the server's own words, shown as examples in the description of <c>task</c>: <c>["rename a symbol", "find unused code"]</c>.</summary>
    /// <remarks>
    /// A model asks in the words it is shown. The SDK's own example, "push the branch", was Snail.MCP.Git's and steered a
    /// model at a file system or a renderer towards words no tool of theirs uses; without examples the description shows
    /// none. Two or three short ones: every session reads them.
    /// </remarks>
    public IList<string> Examples { get; set; } = [];

    /// <summary>What a successful answer of <c>find_tool</c> carries as its structured content; the data alone unless set.</summary>
    /// <remarks>
    /// <see cref="AnswerShape.Envelope"/> for a server whose tools state the envelope as their output schema
    /// (Snail.MCP.FileSystem). When any other tool of the server states an output schema, <c>find_tool</c> states the
    /// schema of this shape too; <see cref="AnswerShape.None"/> states none.
    /// </remarks>
    public AnswerShape Shape { get; set; } = AnswerShape.Data;
}
