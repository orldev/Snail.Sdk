using Microsoft.Extensions.DependencyInjection;

namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>Whether a session may call a tool: the one question the door and every forwarder ask.</summary>
/// <remarks>
/// The default gate reads <see cref="SessionGrant"/> from the services of the request. A server whose sessions are
/// granted tools another way — scopes of its own, as Snail.MCP.AfterEffects and Snail.MCP.Blender keep — registers its
/// own gate, and the door, the list and <see cref="ToolForwarding"/> all ask it instead.
/// </remarks>
public interface IToolGate
{
    /// <summary>Why the session served with <paramref name="services"/> may not call the tool, or null when it may.</summary>
    /// <remarks>
    /// The name is a tool's — or, asked for a program's command that stands for no tool (<c>IProgramCommands.ToolOf</c>
    /// answers null), that command's own name. A gate that grants by scope judges such a name as the command it is
    /// (Snail.MCP.AfterEffects: most of its 129 bridge commands have no tool); the default gate grants it as it grants a
    /// tool, so a session whose address names tools may send it only when the address names it too.
    /// </remarks>
    /// <param name="tool">The tool's name as the protocol lists it, or the name of a command that stands for no tool.</param>
    /// <param name="services">The services of the request — or of the session, where requests share them.</param>
    string? Refusal(string tool, IServiceProvider services);
}

/// <summary>The gate of <see cref="SessionGrant"/>: what the address named, and nothing when it named nothing unless a grant is required.</summary>
internal sealed class GrantGate(GrantOptions options) : IToolGate
{
    public string? Refusal(string tool, IServiceProvider services)
    {
        if (services.GetService<SessionGrant>() is not { } grant)
            return null;

        var isRequired = options.RequiresGrant && grant.IsAddressed && grant.All.Count == 0;

        return grant.Allows(tool) && !isRequired ? null : grant.Refusal(tool);
    }
}
