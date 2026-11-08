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

    private static ServiceProvider Services(ISessionAddress address) => new ServiceCollection().AddSingleton(address).BuildServiceProvider();

    private static bool IsTask(string name) =>
        name.Length > 0 && name.All(letter => char.IsAsciiLetterLower(letter) || char.IsAsciiDigit(letter) || letter == '-');
}
