using Microsoft.Extensions.DependencyInjection;

namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>Whose a job is, named from the session a request is served in: the owners <c>WithJobs(server, ownerOf)</c> takes.</summary>
/// <remarks>
/// A server many clients share gives its jobs an owner so one client does not read another's job by its URI, and the
/// fleet names that owner in one of two ways: the task of the session's working copy (Snail.MCP.Security, Connector, E2E),
/// here, or the MCP session itself (Snail.MCP.Sandbox), <c>JobOwners.Session</c> of <c>Snail.Sdk.Mcp.Http</c>. A tool
/// starts its job with the same owner the function names for its request, so the two cannot drift apart.
/// </remarks>
public static class JobOwners
{
    /// <summary>The owner of a request: the task of the working copy its session's address names, or none when it names no copy.</summary>
    /// <remarks>
    /// <para>
    /// Every step of a task opens the server with a copy of its own under the task's folder, so the steps of one task share
    /// their jobs and no other task reaches them; a worker that connects again opens a new session and still finds the
    /// job it started. A request that names no copy owns nothing, and sees only the jobs nobody owns. The task is the
    /// folder's name — what <see cref="WorkspaceCopy.Of(string?, string, Func{string, bool})"/> answers — and a tool
    /// starts its jobs with that name.
    /// </para>
    /// <para>
    /// The address is <see cref="ISessionAddress"/> of the request's services, which <c>RefuseWhatWasNotGranted</c>,
    /// <c>WithSessionRoots</c> and the HTTP transport register; a server that registers none is refused with that said,
    /// since an owner read from nowhere would quietly leave every job to whoever holds its URI.
    /// </para>
    /// </remarks>
    /// <param name="workspaces">The folder every task's copies are under, read from the server's services: its options.</param>
    /// <param name="isTaskId">Whether a folder's name is a task's id.</param>
    public static Func<IServiceProvider, string?> TaskOfCopy(Func<IServiceProvider, string> workspaces, Func<string, bool> isTaskId)
    {
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(isTaskId);

        return services =>
        {
            var address = services.GetService<ISessionAddress>()
                ?? throw new InvalidOperationException("JobOwners.TaskOfCopy reads the session's address, and no ISessionAddress is registered: call RefuseWhatWasNotGranted, WithSessionRoots or WithSessionAddress.");
            var copy = WorkspaceCopy.Of(address.Values(SessionRoots.Parameter).FirstOrDefault(), workspaces(services), isTaskId);

            return copy.IsCopy ? copy.Task : null;
        };
    }
}
