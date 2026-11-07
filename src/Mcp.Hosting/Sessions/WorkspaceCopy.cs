namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>A working copy under the workspaces folder, and the task it belongs to.</summary>
/// <param name="Task">The task, as the copy's first folder under the workspaces folder names it.</param>
/// <param name="Path">The copy itself: <c>/workspaces/&lt;task&gt;/step-3</c>.</param>
public sealed record WorkspacePlace(string Task, string Path);

/// <summary>The task a session's working copy belongs to, read from where the copy lies under the workspaces folder.</summary>
/// <remarks>
/// The platform names a step's copy in the address — <c>/mcp?root=/workspaces/&lt;task&gt;/step-3</c> — and passes nothing
/// else about the task, so its first folder is the task: every step of a task lands in the same scan history, journal or
/// environment. Snail.MCP.Security, Connector and E2E each kept this as a <c>SessionCopy</c> of their own.
/// </remarks>
public static class WorkspaceCopy
{
    /// <summary>The copy <paramref name="root"/> names strictly under <paramref name="workspaces"/>, or null when it names none there.</summary>
    /// <remarks>
    /// Null for a blank root, the workspaces folder itself, a path outside it, a <c>.</c> or <c>..</c> among its names, or a
    /// first folder <paramref name="isTaskId"/> does not take for a task. Read from the text alone, links not followed:
    /// what is served from the copy mounts it from a volume that holds nothing else.
    /// </remarks>
    /// <param name="root">The root the address named.</param>
    /// <param name="workspaces">The folder every task's copies are under: <c>/workspaces</c>.</param>
    /// <param name="isTaskId">Whether a folder's name is a task's id.</param>
    public static WorkspacePlace? Of(string? root, string workspaces, Func<string, bool> isTaskId)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(isTaskId);

        if (string.IsNullOrWhiteSpace(root))
            return null;

        var under = workspaces.Trim().TrimEnd('/');
        var named = root.Trim();
        var relative = named.StartsWith($"{under}/", StringComparison.Ordinal) ? named[(under.Length + 1)..].Trim('/') : string.Empty;
        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Any(segment => segment is "." or "..") || !isTaskId(segments[0]))
            return null;

        return new WorkspacePlace(segments[0], $"{under}/{string.Join('/', segments)}");
    }
}
