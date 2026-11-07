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
    /// <param name="tool">The tool's name as the protocol lists it.</param>
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
