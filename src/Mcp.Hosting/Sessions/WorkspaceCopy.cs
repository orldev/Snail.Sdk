using System.Diagnostics.CodeAnalysis;

namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>Why a root names no working copy under the workspaces folder.</summary>
/// <remarks>
/// Each a different thing to tell the caller: a session that named no copy is told where a copy comes from, one that named
/// a folder elsewhere that it is not a task's copy. Snail.MCP.Security, Connector and E2E each checked the blank root
/// themselves before asking, because a bare null said neither.
/// </remarks>
public enum CopyRefusal
{
    /// <summary>The root is a task's copy.</summary>
    None,

    /// <summary>The session named no root: blank or absent.</summary>
    NoRoot,

    /// <summary>The root is not strictly under the workspaces folder: elsewhere, a sibling, or the workspaces folder itself.</summary>
    Outside,

    /// <summary>A <c>.</c> or <c>..</c> among the root's names, which could climb out of the copy it seems to name.</summary>
    Malformed,

    /// <summary>The root's first folder under the workspaces folder is not a task the server takes.</summary>
    NotATask,
}

/// <summary>The working copy a root names under the workspaces folder and the task it belongs to, or why it names none.</summary>
/// <typeparam name="TTask">The task as the server holds it: its id, or a type of its own read from the id.</typeparam>
/// <param name="Task">The task, read from the copy's first folder under the workspaces folder; set when it is a copy.</param>
/// <param name="Path">The copy itself — <c>/workspaces/&lt;task&gt;/step-3</c> — set when it is a copy.</param>
/// <param name="Refusal">Why the root names no copy; <see cref="CopyRefusal.None"/> when it names one.</param>
public sealed record WorkspaceCopy<TTask>(TTask? Task, string? Path, CopyRefusal Refusal)
{
    /// <summary>Whether the root names a task's copy; <see cref="Task"/> and <see cref="Path"/> are then set.</summary>
    [MemberNotNullWhen(true, nameof(Task), nameof(Path))]
    public bool IsCopy => Refusal == CopyRefusal.None;
}

/// <summary>The task a session's working copy belongs to, read from where the copy lies under the workspaces folder.</summary>
/// <remarks>
/// The platform names a step's copy in the address — <c>/mcp?root=/workspaces/&lt;task&gt;/step-3</c> — and passes nothing
/// else about the task, so its first folder is the task: every step of a task lands in the same scan history, journal or
/// environment. Snail.MCP.Security, Connector and E2E each kept this as a <c>SessionCopy</c> of their own. The words a
/// caller is told stay the server's: it maps <see cref="WorkspaceCopy{TTask}.Refusal"/> to its own.
/// </remarks>
public static class WorkspaceCopy
{
    /// <summary>The copy <paramref name="root"/> names strictly under <paramref name="workspaces"/> with its task's id as the task, or why it names none.</summary>
    /// <param name="root">The root the address named.</param>
    /// <param name="workspaces">The folder every task's copies are under: <c>/workspaces</c>.</param>
    /// <param name="isTaskId">Whether a folder's name is a task's id.</param>
    public static WorkspaceCopy<string> Of(string? root, string workspaces, Func<string, bool> isTaskId)
    {
        ArgumentNullException.ThrowIfNull(isTaskId);

        return Of(root, workspaces, folder => isTaskId(folder) ? folder : null);
    }

    /// <summary>The copy <paramref name="root"/> names strictly under <paramref name="workspaces"/> with its task as the server reads it, or why it names none.</summary>
    /// <remarks>
    /// The task read once, here, rather than its id checked here and parsed again by the server (Snail.MCP.E2E read its
    /// environment from the id twice). Read from the text alone, links not followed: what is served from the copy mounts
    /// it from a volume that holds nothing else.
    /// </remarks>
    /// <param name="root">The root the address named.</param>
    /// <param name="workspaces">The folder every task's copies are under: <c>/workspaces</c>.</param>
    /// <param name="task">The task a folder's name is, or null when it is none.</param>
    public static WorkspaceCopy<TTask> Of<TTask>(string? root, string workspaces, Func<string, TTask?> task)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(task);

        if (string.IsNullOrWhiteSpace(root))
            return Refused<TTask>(CopyRefusal.NoRoot);

        var under = workspaces.Trim().TrimEnd('/');
        var named = root.Trim();
        var relative = named.StartsWith($"{under}/", StringComparison.Ordinal) ? named[(under.Length + 1)..].Trim('/') : string.Empty;
        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0)
            return Refused<TTask>(CopyRefusal.Outside);

        if (segments.Any(segment => segment is "." or ".."))
            return Refused<TTask>(CopyRefusal.Malformed);

        if (task(segments[0]) is not { } found)
            return Refused<TTask>(CopyRefusal.NotATask);

        return new WorkspaceCopy<TTask>(found, $"{under}/{string.Join('/', segments)}", CopyRefusal.None);
    }

    /// <summary>The copy <paramref name="root"/> names strictly under <paramref name="workspaces"/> with its task as a value the server reads, or why it names none.</summary>
    /// <remarks>
    /// <see cref="Of{TTask}(string?, string, Func{string, TTask})"/> for a task that is a value — a number, a
    /// <see cref="Guid"/>, a struct of the server's — read as null when the folder is none. Through the other overload the
    /// task came out <c>Nullable&lt;T&gt;</c>, and stayed so after <see cref="WorkspaceCopy{TTask}.IsCopy"/>, which can vouch
    /// for a reference but not unwrap a value: every use wrote <c>.Value</c> (Snail.MCP.E2E). Here the task is the value
    /// itself, set when the root is a copy and the default otherwise.
    /// </remarks>
    /// <param name="root">The root the address named.</param>
    /// <param name="workspaces">The folder every task's copies are under: <c>/workspaces</c>.</param>
    /// <param name="task">The task a folder's name is, or null when it is none.</param>
    public static WorkspaceCopy<TTask> Of<TTask>(string? root, string workspaces, Func<string, TTask?> task)
        where TTask : struct
    {
        ArgumentNullException.ThrowIfNull(task);

        var copy = Of<TTask?>(root, workspaces, folder => task(folder));

        return copy.IsCopy ? new WorkspaceCopy<TTask>(copy.Task.Value, copy.Path, CopyRefusal.None) : Refused<TTask>(copy.Refusal);
    }

    private static WorkspaceCopy<TTask> Refused<TTask>(CopyRefusal refusal) => new(default, null, refusal);
}
