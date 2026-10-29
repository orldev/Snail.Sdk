using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The JSON options a server's tools are registered with: the MCP SDK's own, with the server's types put first.</summary>
/// <remarks>
/// <para>
/// A native build cannot look a type up at run time, and with PublishAot on neither can the same server run as a dll:
/// reflection-based serialization is off everywhere. Every type a tool takes or gives beyond the primitives goes into a
/// source-generated JsonSerializerContext of the server, and the context goes in here:
/// <c>.WithTools&lt;MyTools&gt;(ToolJson.Options(MyJson.Default))</c>. A missing type fails when the tool is listed —
/// in a test, before any build is published.
/// </para>
/// <para>Traps already met, kept here so the next server does not meet them again:</para>
/// <list type="bullet">
/// <item>An <c>init</c> property with a default — a side of <c>"auto"</c>, a shape of <c>"rectangle"</c>, an empty list —
/// arrives as null when the field is left out: the generated code sets every init property at construction. Such
/// properties are <c>set</c>.</item>
/// <item>A tool answers with a concrete type: the SDK serializes a result by its type at run time, and the list a collection
/// expression makes is a compiler-made type the generated code refuses to choose an interface for.</item>
/// <item>A static property of the context's own built from <c>Default</c> may run before <c>Default</c> exists; take the
/// options from here, built on first use, instead.</item>
/// </list>
/// </remarks>
public static class ToolJson
{
    /// <summary>
    /// The SDK's options with this resolver — a server's generated context — consulted first, and the job report of
    /// this library known too, so a tool that answers with a job needs no line for it.
    /// </summary>
    public static JsonSerializerOptions Options(IJsonTypeInfoResolver context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);

        options.TypeInfoResolverChain.Insert(0, Jobs.JobJson.Default);
        options.TypeInfoResolverChain.Insert(0, context);
        options.MakeReadOnly();

        return options;
    }
}
