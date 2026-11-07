namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>How a server's skills are named and how long one stays loaded unused.</summary>
public sealed class SkillOptions
{
    /// <summary>The server's short name, which names the skill tools: <c>&lt;name&gt;_skills</c>, <c>&lt;name&gt;_enable_skill</c>, <c>&lt;name&gt;_disable_skill</c>.</summary>
    /// <remarks>
    /// Read from the command Snail.Sdk.Mcp writes into the server's assembly (<see cref="ServerSettings.NameOf"/>) unless
    /// set: <c>s3l-mcp-blender</c> is <c>blender</c>. A program built otherwise, a test among them, sets it.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>How long a skill stays loaded in a session that calls none of its tools; never unloaded when zero, as unless set.</summary>
    /// <remarks>A long session otherwise keeps every skill it ever loaded in the model's context. The servers that unload set it from their settings.</remarks>
    public TimeSpan IdleTimeout { get; set; }
}
