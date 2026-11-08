using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The owner of a request's jobs named from its session: the task of the copy its address names.</summary>
public sealed class JobOwnersTests
{
    private static readonly Func<IServiceProvider, string?> TaskOfCopy = JobOwners.TaskOfCopy(_ => "/workspaces", IsTask);

    /// <summary>Snail.MCP.Security, Connector and E2E each read the task of the session's copy to own their jobs by it.</summary>
    [Theory]
    [InlineData("/workspaces/task-1/step-3", "task-1")]
    [InlineData("/workspaces/task-1", "task-1")]
    public void Owner_OfASessionAtATasksCopy_IsTheTask(string root, string task)
    {
        using var services = Services(new FixedAddress((SessionRoots.Parameter, root)));

        Assert.Equal(task, TaskOfCopy(services));
    }

    /// <summary>Two steps of one task, each at a copy of its own, share their jobs: a worker that connects again finds what it started.</summary>
    [Fact]
    public void Owner_OfTwoStepsOfOneTask_IsTheSame()
    {
        using var first = Services(new FixedAddress((SessionRoots.Parameter, "/workspaces/task-1/step-1")));
        using var second = Services(new FixedAddress((SessionRoots.Parameter, "/workspaces/task-1/step-2")));

        Assert.Equal(TaskOfCopy(first), TaskOfCopy(second));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/etc")]
    [InlineData("/workspaces/task-1/../task-2")]
    [InlineData("/workspaces/Not_A_Task")]
    public void Owner_OfASessionAtNoTasksCopy_IsNobody(string? root)
    {
        ISessionAddress address = root is null ? SessionAddress.None : new FixedAddress((SessionRoots.Parameter, root));
        using var services = Services(address);

        Assert.Null(TaskOfCopy(services));
    }

    /// <summary>An owner read from nowhere would quietly leave every job to whoever holds its URI.</summary>
    [Fact]
    public void Owner_WithNoAddressRegistered_IsRefusedSayingWhatToCall()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var refused = Assert.Throws<InvalidOperationException>(() => TaskOfCopy(services));

        Assert.Contains("RefuseWhatWasNotGranted", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>#125: the overload of an id always gave the folder's name; a server with a task type of its own names the owner itself.</summary>
    [Fact]
    public void Owner_OfAServersOwnTask_IsWhatTheServerNamesIt()
    {
        var ownerOf = JobOwners.TaskOfCopy(_ => "/workspaces", folder => IsTask(folder) ? new Ticket(folder.ToUpperInvariant()) : null, ticket => ticket.Id);
        using var copy = Services(new FixedAddress((SessionRoots.Parameter, "/workspaces/task-1/step-3")));
        using var elsewhere = Services(new FixedAddress((SessionRoots.Parameter, "/etc")));

        Assert.Equal("TASK-1", ownerOf(copy));
        Assert.Null(ownerOf(elsewhere));
    }

    /// <summary>A task that is a value reaches the owner as the value itself, not Nullable (Snail.MCP.E2E's TaskEnvironment).</summary>
    [Fact]
    public void Owner_OfATaskThatIsAValue_IsNamedFromTheValue()
    {
        static Label? LabelOf(string folder) => IsTask(folder) ? new Label($"e2e-{folder}") : null;
        var ownerOf = JobOwners.TaskOfCopy(_ => "/workspaces", LabelOf, label => label.Value);
        using var copy = Services(new FixedAddress((SessionRoots.Parameter, "/workspaces/task-1")));
        using var nobody = Services(new FixedAddress((SessionRoots.Parameter, "/workspaces/Not_A_Task")));

        Assert.Equal("e2e-task-1", ownerOf(copy));
        Assert.Null(ownerOf(nobody));
    }

    [Fact]
    public void Owner_OfAServersOwnTask_WithNoAddressRegistered_IsRefusedSayingWhatToCall()
    {
        var ownerOf = JobOwners.TaskOfCopy(_ => "/workspaces", folder => new Ticket(folder), ticket => ticket.Id);
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => ownerOf(services));
    }

    private sealed record Ticket(string Id);

    private readonly record struct Label(string Value);

    private static ServiceProvider Services(ISessionAddress address) => new ServiceCollection().AddSingleton(address).BuildServiceProvider();

    private static bool IsTask(string name) =>
        name.Length > 0 && name.All(letter => char.IsAsciiLetterLower(letter) || char.IsAsciiDigit(letter) || letter == '-');
}
