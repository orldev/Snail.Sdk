using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The folders a session works in, every link on a path followed before it is judged.</summary>
public sealed class SessionRootsTests
{
    [Fact]
    public void Roots_OfASessionThatNamesNone_AreTheVolumes()
    {
        var volume = TestFolder.Create();

        var roots = new SessionRoots(new FixedAddress(("tool", "read_file")), [volume]);

        Assert.Equal([volume], roots.All);
    }

    /// <summary>Adding the volume to what the session named would hand every session every other task's files.</summary>
    [Fact]
    public void Roots_OfASessionThatNamesOne_AreThatOneAlone()
    {
        var volume = TestFolder.Create();
        var copy = Directory.CreateDirectory(Path.Combine(volume, "task-1")).FullName;

        var roots = new SessionRoots(new FixedAddress(("root", $"{copy}/")), [volume]);

        Assert.Equal([copy], roots.All);
        Assert.False(roots.IsOutside);
        Assert.False(roots.IsAbsent);
    }

    /// <summary>A session opened at ?root=/ was served the whole disk of the host.</summary>
    [Fact]
    public void Root_OutsideTheVolumes_IsOutside_AndReachesNothing()
    {
        var volume = TestFolder.Create();
        var elsewhere = TestFolder.Create();

        var roots = new SessionRoots(new FixedAddress(("root", elsewhere)), [volume]);

        Assert.True(roots.IsOutside);
        Assert.Equal([elsewhere], roots.Outside);
        Assert.Empty(roots.All);
        Assert.False(roots.Reach(Path.Combine(elsewhere, "a.txt")).IsAllowed);
    }

    [Fact]
    public void Root_ThatIsALinkOutOfTheVolume_IsOutside()
    {
        var volume = TestFolder.Create();
        var elsewhere = TestFolder.Create();
        var link = Path.Combine(volume, "task-1");
        Directory.CreateSymbolicLink(link, elsewhere);

        var roots = new SessionRoots(new FixedAddress(("root", link)), [volume]);

        Assert.True(roots.IsOutside);
    }

    [Fact]
    public void Root_MissingOnThisMachine_IsAbsent()
    {
        var volume = TestFolder.Create();
        var missing = Path.Combine(volume, "task-on-another-host");

        var roots = new SessionRoots(new FixedAddress(("root", missing)), [volume]);

        Assert.True(roots.IsAbsent);
        Assert.Equal([missing], roots.Absent);
    }

    [Fact]
    public void Reach_OfARelativePath_IsTheRealPathUnderTheFirstRoot()
    {
        var copy = TestFolder.Create();
        File.WriteAllText(Path.Combine(copy, "a.txt"), "a");

        var reached = new SessionRoots(new FixedAddress(("root", copy)), []).Reach("a.txt");

        Assert.True(reached.IsAllowed);
        Assert.Equal(Path.Combine(RealPath.Of(copy)!, "a.txt"), reached.Path);
    }

    [Fact]
    public void Reach_OfAFileNotWrittenYet_IsAllowed_UnderItsRealFolder()
    {
        var copy = TestFolder.Create();

        var reached = new SessionRoots(new FixedAddress(("root", copy)), []).Reach("new/b.txt");

        Assert.Equal(Path.Combine(RealPath.Of(copy)!, "new", "b.txt"), reached.Path);
    }

    [Fact]
    public void Reach_ThroughALinkInTheMiddleOfThePath_ThatLeadsOut_IsRefused()
    {
        var copy = TestFolder.Create();
        var elsewhere = TestFolder.Create();
        File.WriteAllText(Path.Combine(elsewhere, "secret.txt"), "s");
        Directory.CreateSymbolicLink(Path.Combine(copy, "out"), elsewhere);

        var reached = new SessionRoots(new FixedAddress(("root", copy)), []).Reach("out/secret.txt");

        Assert.False(reached.IsAllowed);
        Assert.StartsWith("'out/secret.txt' is outside this session, which works in", reached.Refusal, StringComparison.Ordinal);
    }

    /// <summary>The fleet's copies took a missing target for the end of the walk, and a write through the link created the file outside.</summary>
    [Fact]
    public void Reach_ThroughALinkToSomethingNotThereYet_OutsideTheRoot_IsRefused()
    {
        var copy = TestFolder.Create();
        var elsewhere = TestFolder.Create();
        File.CreateSymbolicLink(Path.Combine(copy, "planted"), Path.Combine(elsewhere, "new.txt"));

        var reached = new SessionRoots(new FixedAddress(("root", copy)), []).Reach("planted");

        Assert.False(reached.IsAllowed);
    }

    [Fact]
    public void Reach_ThroughALinkThatStaysInside_IsAllowed_AtWhereItLeads()
    {
        var copy = TestFolder.Create();
        var src = Directory.CreateDirectory(Path.Combine(copy, "src")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(copy, "code"), "src");

        var reached = new SessionRoots(new FixedAddress(("root", copy)), []).Reach("code/App.cs");

        Assert.Equal(Path.Combine(RealPath.Of(src)!, "App.cs"), reached.Path);
    }

    [Fact]
    public void Reach_ThroughALoopOfLinks_IsRefused_WithTheReason()
    {
        var copy = TestFolder.Create();
        File.CreateSymbolicLink(Path.Combine(copy, "a"), Path.Combine(copy, "b"));
        File.CreateSymbolicLink(Path.Combine(copy, "b"), Path.Combine(copy, "a"));

        var reached = new SessionRoots(new FixedAddress(("root", copy)), []).Reach("a/x.txt");

        Assert.False(reached.IsAllowed);
        Assert.Contains("cannot be followed", reached.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Reach_OfASiblingWhoseNameBeginsLikeTheRoot_IsRefused()
    {
        var copy = TestFolder.Create();
        var sibling = Directory.CreateDirectory($"{copy}-x").FullName;

        try
        {
            var reached = new SessionRoots(new FixedAddress(("root", copy)), []).Reach(Path.Combine(sibling, "a.txt"));

            Assert.False(reached.IsAllowed);
        }
        finally
        {
            Directory.Delete(sibling);
        }
    }

    /// <summary>A root or a path no file can have is a refusal, not an exception a client could raise at will.</summary>
    [Fact]
    public void RootOrPath_WithANul_IsLeftOut_OrRefused()
    {
        var copy = TestFolder.Create();

        var named = new SessionRoots(new FixedAddress(("root", $"{copy}\0x")), []).Named;
        var reached = new SessionRoots(new FixedAddress(("root", copy)), []).Reach("a\0b");

        Assert.Empty(named);
        Assert.False(reached.IsAllowed);
    }

    [Fact]
    public void Reach_WithNoRootAndNoVolume_ReachesNothing()
    {
        var reached = new SessionRoots(new FixedAddress(), []).Reach("/etc/hosts");

        Assert.Equal("this session was given no root, so nothing is reachable", reached.Refusal);
    }

    /// <summary>A root behind a link, /var on macOS among them, made every answer name the host's real path.</summary>
    [Fact]
    public void Relative_OfAReachedPath_IsRelativeToTheFirstRoot()
    {
        var copy = TestFolder.Create();
        var roots = new SessionRoots(new FixedAddress(("root", copy)), []);

        var reached = roots.Reach("src/App.cs");

        Assert.Equal(Path.Combine("src", "App.cs"), roots.Relative(reached.Path!));
    }

    [Fact]
    public void RealPath_FollowsARelativeLink_FromTheLinksFolder()
    {
        var folder = TestFolder.Create();
        var target = Directory.CreateDirectory(Path.Combine(folder, "target", "inner")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(folder, "link"), Path.Combine("target", "inner"));

        Assert.Equal(RealPath.Of(target), RealPath.Of(Path.Combine(folder, "link")));
    }

    /// <summary>A .. after a link climbs from where the link led, as the system does, not from the name the link had.</summary>
    [Fact]
    public void RealPath_ClimbsFromWhereALinkLed()
    {
        var folder = TestFolder.Create();
        var elsewhere = TestFolder.Create();
        Directory.CreateDirectory(Path.Combine(elsewhere, "inner"));
        Directory.CreateSymbolicLink(Path.Combine(folder, "link"), Path.Combine(elsewhere, "inner"));

        Assert.Equal(Path.Combine(RealPath.Of(elsewhere)!, "x"), RealPath.Of(Path.Combine(folder, "link", "..", "x")));
    }

    /// <summary>The folder is taken by its real path, since the temporary folder itself is behind a link on macOS and that link counts too.</summary>
    [Fact]
    public void RealPath_OfAChainLongerThanTheLimit_IsUnknown_AndOfOneWithin_IsTheEnd()
    {
        var folder = RealPath.Of(TestFolder.Create())!;
        var end = Directory.CreateDirectory(Path.Combine(folder, "end")).FullName;
        var previous = end;

        for (var link = 0; link < 3; link++)
        {
            var next = Path.Combine(folder, $"link-{link}");
            Directory.CreateSymbolicLink(next, previous);
            previous = next;
        }

        Assert.Null(RealPath.Of(previous, maxLinks: 2));
        Assert.Equal(RealPath.Of(end), RealPath.Of(previous, maxLinks: 3));
    }

    [Fact]
    public void RealPath_OfALoop_IsUnknown()
    {
        var folder = TestFolder.Create();
        File.CreateSymbolicLink(Path.Combine(folder, "loop"), Path.Combine(folder, "loop"));

        Assert.Null(RealPath.Of(Path.Combine(folder, "loop", "x")));
    }

    [Theory]
    [InlineData("/workspaces/task-1", "task-1", "/workspaces/task-1")]
    [InlineData("/workspaces/task-1/step-3/", "task-1", "/workspaces/task-1/step-3")]
    [InlineData("/workspaces//task-1//step-3", "task-1", "/workspaces/task-1/step-3")]
    public void WorkspaceCopy_UnderTheWorkspaces_IsTheTasksCopy(string root, string task, string path) =>
        Assert.Equal(new WorkspacePlace(task, path), WorkspaceCopy.Of(root, "/workspaces/", IsTask));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("/workspaces")]
    [InlineData("/workspaces/")]
    [InlineData("/workspaces-other/task-1")]
    [InlineData("/etc")]
    [InlineData("/workspaces/task-1/../task-2")]
    [InlineData("/workspaces/./task-1")]
    [InlineData("/workspaces/Not_A_Task/step")]
    public void WorkspaceCopy_ElsewhereOrMalformed_IsNone(string? root) =>
        Assert.Null(WorkspaceCopy.Of(root, "/workspaces", IsTask));

    private static bool IsTask(string name) =>
        name.Length > 0 && name.All(letter => char.IsAsciiLetterLower(letter) || char.IsAsciiDigit(letter) || letter == '-');
}
