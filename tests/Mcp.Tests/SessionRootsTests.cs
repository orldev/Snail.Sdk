using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Hosting.Answers;
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
        Assert.Equal(new WorkspaceCopy<string>(task, path, CopyRefusal.None), WorkspaceCopy.Of(root, "/workspaces/", IsTask));

    /// <summary>Each server told a session that named no copy something else than one that named a folder elsewhere, and checked the blank root itself to tell them apart.</summary>
    [Theory]
    [InlineData(null, CopyRefusal.NoRoot)]
    [InlineData(" ", CopyRefusal.NoRoot)]
    [InlineData("/workspaces", CopyRefusal.Outside)]
    [InlineData("/workspaces/", CopyRefusal.Outside)]
    [InlineData("/workspaces-other/task-1", CopyRefusal.Outside)]
    [InlineData("/etc", CopyRefusal.Outside)]
    [InlineData("/workspaces/task-1/../task-2", CopyRefusal.Malformed)]
    [InlineData("/workspaces/./task-1", CopyRefusal.Malformed)]
    [InlineData("/workspaces/Not_A_Task/step", CopyRefusal.NotATask)]
    public void WorkspaceCopy_ElsewhereOrMalformed_SaysWhy(string? root, CopyRefusal why)
    {
        var copy = WorkspaceCopy.Of(root, "/workspaces", IsTask);

        Assert.False(copy.IsCopy);
        Assert.Equal(why, copy.Refusal);
        Assert.Null(copy.Task);
        Assert.Null(copy.Path);
    }

    /// <summary>Snail.MCP.E2E checked the id here and read its environment from it again: the task is read once, a value type among them.</summary>
    [Fact]
    public void WorkspaceCopy_OfTheServersOwnTask_IsReadOnce()
    {
        int reads = 0;
        TaskLabel? Label(string folder)
        {
            reads++;

            return IsTask(folder) ? new TaskLabel($"e2e-{folder}") : null;
        }

        var copy = WorkspaceCopy.Of("/workspaces/task-1/step-3", "/workspaces", Label);
        var refused = WorkspaceCopy.Of("/workspaces/Not_A_Task", "/workspaces", Label);

        Assert.True(copy.IsCopy);
        Assert.Equal(new TaskLabel("e2e-task-1"), copy.Task);
        Assert.Equal("/workspaces/task-1/step-3", copy.Path);
        Assert.Equal(CopyRefusal.NotATask, refused.Refusal);
        Assert.Equal(2, reads);
    }

    /// <summary>#124: a task that is a value came out Nullable and stayed so after IsCopy, so every use wrote .Value; it is the value itself now.</summary>
    [Fact]
    public void WorkspaceCopy_OfATaskThatIsAValue_IsThatValue_AndTheDefaultWhenRefused()
    {
        static int? Number(string folder) => int.TryParse(folder, CultureInfo.InvariantCulture, out var number) ? number : null;

        WorkspaceCopy<int> copy = WorkspaceCopy.Of("/workspaces/42/step-3", "/workspaces", Number);
        WorkspaceCopy<int> refused = WorkspaceCopy.Of("/workspaces/not-a-number", "/workspaces", Number);

        Assert.True(copy.IsCopy);
        Assert.Equal(42, copy.Task);
        Assert.Equal("/workspaces/42/step-3", copy.Path);
        Assert.Equal(new WorkspaceCopy<int>(0, null, CopyRefusal.NotATask), refused);
    }

    /// <summary>#128: Security, Connector and E2E each wrote a CopyPlace to carry the copy through an Answer, its task and path nullable on WorkspaceCopy.</summary>
    [Fact]
    public void ToAnswer_OfACopy_IsItsPlace_WithTaskAndPathSet()
    {
        var answer = WorkspaceCopy.Of("/workspaces/task-1/step-3", "/workspaces", IsTask).ToAnswer(_ => Answer.Failed("never asked"));

        Assert.True(answer.TryGet(out var place));
        Assert.Equal(new WorkspacePlace<string>("task-1", "/workspaces/task-1/step-3"), place);
    }

    /// <summary>The words are the server's, and a failure that names no type of its own is typed by the reason, as a path refused is.</summary>
    [Theory]
    [InlineData(null, "no copy", "noRoot")]
    [InlineData("/etc", "elsewhere", "outside")]
    [InlineData("/workspaces/task-1/../task-2", "elsewhere", "malformed")]
    [InlineData("/workspaces/Not_A_Task", "elsewhere", "notATask")]
    public void ToAnswer_OfNoCopy_IsTheServersWords_TypedByTheReason(string? root, string error, string type)
    {
        var answer = WorkspaceCopy.Of(root, "/workspaces", IsTask)
            .ToAnswer(refusal => refusal == CopyRefusal.NoRoot ? Answer.Failed("no copy", "open the session at a copy") : Answer.Failed("elsewhere"));

        Assert.False(answer.IsSuccess);
        Assert.Equal((error, type), (answer.Error, answer.Type));
    }

    [Fact]
    public void ToAnswer_OfNoCopy_KeepsTheTypeTheServerGave()
    {
        var answer = WorkspaceCopy.Of("/etc", "/workspaces", IsTask).ToAnswer(_ => Answer.Failed("elsewhere", type: "notACopy"));

        Assert.Equal("notACopy", answer.Type);
    }

    /// <summary>A task that is a value is carried as the value, as WorkspaceCopy carries it.</summary>
    [Fact]
    public void ToAnswer_OfATaskThatIsAValue_CarriesTheValue()
    {
        static int? Number(string folder) => int.TryParse(folder, CultureInfo.InvariantCulture, out var number) ? number : null;

        Answer<WorkspacePlace<int>> answer = WorkspaceCopy.Of("/workspaces/42", "/workspaces", Number).ToAnswer(_ => Answer.Failed("never asked"));

        Assert.Equal(42, answer.Value!.Task);
    }

    /// <summary>Snail.MCP.Documents' benchmark kept a <c>NoAddress</c> of its own to build the roots of a program.</summary>
    [Fact]
    public void Roots_WithNoAddress_AreTheVolumes()
    {
        var volume = TestFolder.Create();

        var roots = new SessionRoots(SessionAddress.None, [volume]);

        Assert.False(SessionAddress.None.IsPresent);
        Assert.Empty(SessionAddress.None.Values(SessionRoots.Parameter));
        Assert.Equal([volume], roots.All);
    }

    [Fact]
    public void Reach_OfAFile_WhereAFileMustBe_IsAllowed_AndOfAFolderOrNothing_IsRefusedInTheFleetsWords()
    {
        var root = TestFolder.Create();
        File.WriteAllText(Path.Combine(root, "notes.txt"), "the note");
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        var roots = new SessionRoots(SessionAddress.None, [root]);

        var file = roots.Reach("notes.txt", PathKind.File);
        var folder = roots.Reach("docs", PathKind.File);
        var nothing = roots.Reach("missing.txt", PathKind.File);

        Assert.Equal(RealPath.Of(Path.Combine(root, "notes.txt")), file.Path);
        Assert.Equal(("'docs' is not a file", "that is a folder: name a file inside it"), (folder.Refusal, folder.Hint));
        Assert.Equal(("there is no file 'missing.txt'", "check the name and the folder it is in"), (nothing.Refusal, nothing.Hint));
    }

    [Fact]
    public void Reach_OfAFolder_WhereAFolderMustBe_IsAllowed_AndOfAFileOrNothing_IsRefused()
    {
        var root = TestFolder.Create();
        File.WriteAllText(Path.Combine(root, "notes.txt"), "the note");
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        var roots = new SessionRoots(SessionAddress.None, [root]);

        var folder = roots.Reach("docs", PathKind.Folder);
        var file = roots.Reach("notes.txt", PathKind.Folder);
        var nothing = roots.Reach("missing", PathKind.Folder);
        var anything = roots.Reach("missing");

        Assert.Equal(RealPath.Of(Path.Combine(root, "docs")), folder.Path);
        Assert.Equal(("'notes.txt' is not a folder", "that is a file: name the folder it is in"), (file.Refusal, file.Hint));
        Assert.Equal("there is no folder 'missing'", nothing.Refusal);
        Assert.True(anything.IsAllowed);
    }

    /// <summary>A hint that names the variable is one the operator acts on without reading the server's documentation.</summary>
    [Fact]
    public void Reach_WithNoRoot_NamesTheServersRootsSetting_WhenItGaveOne()
    {
        var named = new SessionRoots(SessionAddress.None, [], new SessionRootsOptions { RootsSetting = "SNAIL_MCP_DOCUMENTS_ROOTS__0" });
        var unnamed = new SessionRoots(SessionAddress.None, []);

        Assert.EndsWith("or set SNAIL_MCP_DOCUMENTS_ROOTS__0", named.Reach("notes.txt").Hint, StringComparison.Ordinal);
        Assert.EndsWith("or configure the server's roots", unnamed.Reach("notes.txt").Hint, StringComparison.Ordinal);
    }

    /// <summary>A volume of / was the prefix // and held nothing, so Snail.MCP.Roslyn kept a workaround of its own.</summary>
    [Fact]
    public void Volume_OfTheWholeDisk_HoldsTheRootsANamedSessionGives_AndThePathsOfOneThatNamesNone()
    {
        var copy = RealPath.Of(TestFolder.Create())!;
        File.WriteAllText(Path.Combine(copy, "a.txt"), "a");
        var disk = Path.GetPathRoot(copy)!;

        var named = new SessionRoots(new FixedAddress(("root", copy)), [disk]);
        var program = new SessionRoots(SessionAddress.None, [disk]);

        Assert.False(named.IsOutside);
        Assert.Equal([copy], named.All);
        Assert.True(named.Reach("a.txt", PathKind.File).IsAllowed);
        Assert.Equal(Path.Combine(copy, "a.txt"), program.Reach(Path.Combine(copy, "a.txt"), PathKind.File).Path);
        Assert.Equal(Path.Combine("a.txt"), named.Relative(Path.Combine(copy, "a.txt")));
    }

    [Theory]
    [InlineData("/etc/hosts", "/", true)]
    [InlineData("/", "/", true)]
    [InlineData("/workspaces/task-1", "/workspaces/", true)]
    [InlineData("/workspaces", "/workspaces/", true)]
    [InlineData("/workspaces-other/task-1", "/workspaces", false)]
    [InlineData("/workspaces-other", "/workspaces/", false)]
    public void Under_ARootThatEndsInASeparator_IsWhatLiesBelowIt(string path, string root, bool isUnder) =>
        Assert.Equal(isUnder, SessionRoots.IsUnder(path, root));

    /// <summary>Snail.MCP.Roslyn installed in a desktop client with no settings opens whatever solution its caller names.</summary>
    [Fact]
    public void Program_WithNoVolume_ThatReachesEverywhere_ReachesAnyPath_LinksFollowed_AndKindsHeld()
    {
        var elsewhere = TestFolder.Create();
        File.WriteAllText(Path.Combine(elsewhere, "App.sln"), "s");
        var roots = new SessionRoots(SessionAddress.None, [], new SessionRootsOptions { ReachesEverywhereAsAProgram = true });

        var file = roots.Reach(Path.Combine(elsewhere, "App.sln"), PathKind.File);
        var folder = roots.Reach(elsewhere, PathKind.File);
        var relative = roots.Reach("App.sln");

        Assert.True(roots.ReachesEverywhere);
        Assert.Empty(roots.All);
        Assert.Equal(Path.Combine(RealPath.Of(elsewhere)!, "App.sln"), file.Path);
        Assert.Equal(Reached.NotAFile, folder.Type);
        Assert.Equal(RealPath.Of(Path.Combine(Environment.CurrentDirectory, "App.sln")), relative.Path);
    }

    /// <summary>A server over HTTP is reached by whoever reaches its port: the option is for a program alone.</summary>
    [Fact]
    public void Session_AtAnAddress_ThatNamesNoRoot_ReachesNothing_EvenWhereAProgramWouldReachEverywhere()
    {
        var roots = new SessionRoots(new FixedAddress(), [], new SessionRootsOptions { ReachesEverywhereAsAProgram = true });

        var reached = roots.Reach("/etc/hosts");

        Assert.False(roots.ReachesEverywhere);
        Assert.Equal(Reached.NoRoot, reached.Type);
    }

    [Fact]
    public void Program_ThatReachesEverywhere_WithAVolumeConfigured_IsConfinedToIt()
    {
        var volume = TestFolder.Create();
        var elsewhere = TestFolder.Create();
        var roots = new SessionRoots(SessionAddress.None, [volume], new SessionRootsOptions { ReachesEverywhereAsAProgram = true });

        var reached = roots.Reach(Path.Combine(elsewhere, "a.txt"));

        Assert.False(roots.ReachesEverywhere);
        Assert.Equal(Reached.Outside, reached.Type);
    }

    /// <summary>A program branches on the kind of a refusal, not on its words.</summary>
    [Fact]
    public void Refusals_CarryTheirKind()
    {
        var root = TestFolder.Create();
        var elsewhere = TestFolder.Create();
        File.WriteAllText(Path.Combine(root, "notes.txt"), "n");
        File.CreateSymbolicLink(Path.Combine(root, "loop"), Path.Combine(root, "loop"));
        var roots = new SessionRoots(new FixedAddress(("root", root)), []);

        Assert.Equal(Reached.Outside, roots.Reach(Path.Combine(elsewhere, "a.txt")).Type);
        Assert.Equal(Reached.NoPath, roots.Reach(" ").Type);
        Assert.Equal(Reached.Unfollowable, roots.Reach("loop/x").Type);
        Assert.Equal(Reached.Absent, roots.Reach("missing.txt", PathKind.File).Type);
        Assert.Equal(Reached.Absent, roots.Reach("missing", PathKind.Folder).Type);
        Assert.Equal(Reached.NotAFolder, roots.Reach("notes.txt", PathKind.Folder).Type);
        Assert.Equal(Reached.NoRoot, new SessionRoots(SessionAddress.None, []).Reach("a.txt").Type);
        Assert.Equal(Reached.Outside, new SessionRoots(new FixedAddress(("root", elsewhere)), [root]).Reach("a.txt").Type);
        Assert.Null(roots.Reach("notes.txt").Type);
    }

    /// <summary>Snail.MCP.FileSystem wrote <c>Refused(reached)</c> with a <c>??</c> the compiler could not see past; the check now tells it.</summary>
    [Fact]
    public void Refusal_IsAFailedAnswer_WithItsWordsHintAndKind_AndTheCheckTellsTheCompilerWhichIsThere()
    {
        var root = TestFolder.Create();
        var roots = new SessionRoots(new FixedAddress(("root", root)), []);
        var refused = roots.Reach("missing.txt", PathKind.File);
        var allowed = roots.Reach("new.txt");

        var answer = refused.As<int>();
        string why = refused.IsAllowed ? string.Empty : refused.Refusal;
        string path = allowed.IsAllowed ? allowed.Path : string.Empty;

        Assert.Equal(("there is no file 'missing.txt'", "check the name and the folder it is in", Reached.Absent), (answer.Error, answer.Hint, answer.Type));
        Assert.False(answer.IsSuccess);
        Assert.Equal(answer.Error, why);
        Assert.EndsWith("new.txt", path, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => allowed.As<int>());
    }

    [Fact]
    public void Refused_WithoutWords_StillSaysWhy()
    {
        var reached = new Reached(null, null, null);

        Assert.False(reached.IsAllowed);
        Assert.Equal("the path may not be used", reached.Refusal);
    }

    /// <summary>A stateless server has no session to speak of: its address says what a refusal calls the caller.</summary>
    [Fact]
    public void Refusal_SpeaksOfWhatTheAddressSpeaksFor()
    {
        var root = TestFolder.Create();
        var elsewhere = TestFolder.Create();

        var outside = new SessionRoots(new StatelessAddress(("root", root)), []).Reach(Path.Combine(elsewhere, "a.txt"));
        var unrooted = new SessionRoots(new StatelessAddress(), []).Reach("a.txt");

        Assert.StartsWith("'", outside.Refusal, StringComparison.Ordinal);
        Assert.EndsWith($"is outside this request's root {root}, which its address names", outside.Refusal, StringComparison.Ordinal);
        Assert.Equal("this request was given no root, so nothing is reachable", unrooted.Refusal);
    }

    /// <summary>#139: a request on a stateless transport works in nothing; the roots it is held to are what its address named, or the server's.</summary>
    [Fact]
    public void Refusal_OnAStatelessTransport_SaysWhereTheRootsCameFrom()
    {
        var first = TestFolder.Create();
        var second = TestFolder.Create();
        var elsewhere = Path.Combine(TestFolder.Create(), "a.txt");

        var named = new SessionRoots(new StatelessAddress(("root", first), ("root", second)), []).Reach(elsewhere);
        var served = new SessionRoots(new StatelessAddress(), [first]).Reach(elsewhere);
        var session = new SessionRoots(new FixedAddress(("root", first)), []).Reach(elsewhere);

        Assert.Equal($"'{elsewhere}' is outside this request's roots {first}, {second}, which its address names", named.Refusal);
        Assert.Equal($"'{elsewhere}' is outside this server's root {first}", served.Refusal);
        Assert.Equal($"'{elsewhere}' is outside this session, which works in {first}", session.Refusal);
        Assert.Equal(Reached.Outside, named.Type);
    }

    [Fact]
    public void Roots_RegisteredWithoutAnMcpBuilder_AreTheVolumes_AndRegisteredOnce()
    {
        var volume = TestFolder.Create();
        var services = new ServiceCollection();

        services.AddSessionRoots(_ => [volume], options => options.RootsSetting = "SNAIL_MCP_X_ROOTS__0");
        services.AddSessionRoots(_ => ["/elsewhere"]);
        using var provider = services.BuildServiceProvider();

        Assert.Equal([volume], provider.GetRequiredService<SessionRoots>().All);
        Assert.Equal("SNAIL_MCP_X_ROOTS__0", provider.GetRequiredService<SessionRootsOptions>().RootsSetting);
        Assert.Single(services, service => service.ServiceType == typeof(SessionRoots));
        Assert.Same(SessionAddress.None, provider.GetRequiredService<ISessionAddress>());
    }

    /// <summary>#57: a disk that fails while a path is reached is a refusal of its own kind, not the server's failure.</summary>
    [Fact]
    public void Reach_OnADiskThatFails_IsRefusedAsUnreadable_InTheDisksWords()
    {
        var root = TestFolder.Create();
        var disk = new FailingDisk { LinkTarget = _ => throw new IOException("the share did not answer") };
        var roots = new SessionRoots(new FixedAddress(("root", root)), [], new SessionRootsOptions { Disk = disk });

        var reached = roots.Reach("a.txt", PathKind.File);

        Assert.False(reached.IsAllowed);
        Assert.Equal(Reached.Unreadable, reached.Type);
        Assert.Equal("'a.txt' could not be reached: the share did not answer", reached.Refusal);
        Assert.NotNull(reached.Hint);
    }

    [Fact]
    public void Reach_OnADiskThatMayNotBeRead_IsRefusedAsUnreadable()
    {
        var root = TestFolder.Create();
        var disk = new FailingDisk { IsFile = _ => throw new UnauthorizedAccessException("access denied") };
        var roots = new SessionRoots(new FixedAddress(("root", root)), [], new SessionRootsOptions { Disk = disk });

        var reached = roots.Reach("a.txt", PathKind.File);

        Assert.Equal(Reached.Unreadable, reached.Type);
    }

    /// <summary>A folder that is there when the door looks and gone when the call reaches it is absent, not a failure.</summary>
    [Fact]
    public void Reach_OfAFolderThatVanishedAfterTheDoor_IsAbsent()
    {
        var root = TestFolder.Create();
        var asked = 0;
        var disk = new FailingDisk { IsFolder = _ => ++asked == 1 };
        var roots = new SessionRoots(new FixedAddress(("root", root)), [], new SessionRootsOptions { Disk = disk });

        var absent = roots.Absent;
        var reached = roots.Reach(root, PathKind.Folder);

        Assert.Empty(absent);
        Assert.Equal(Reached.Absent, reached.Type);
    }

    [Fact]
    public void Roots_ThatTheDiskCannotAnswerFor_AreAbsent()
    {
        var root = TestFolder.Create();
        var disk = new FailingDisk { IsFolder = _ => throw new IOException("stale handle") };

        var roots = new SessionRoots(new FixedAddress(("root", root)), [], new SessionRootsOptions { Disk = disk });

        Assert.Equal([root], roots.Absent);
    }

    /// <summary>The links of a path are read on the server's disk too: a link it knows of is followed, one the machine has is not asked about.</summary>
    [Fact]
    public void Links_AreReadOnTheServersDisk()
    {
        var root = RealPath.Of(TestFolder.Create())!;
        var elsewhere = RealPath.Of(TestFolder.Create())!;
        var link = Path.Combine(root, "out");
        var disk = new FailingDisk { LinkTarget = path => path == link ? elsewhere : null };
        var roots = new SessionRoots(new FixedAddress(("root", root)), [], new SessionRootsOptions { Disk = disk });

        var reached = roots.Reach("out/secret.txt");

        Assert.Equal(Path.Combine(elsewhere, "secret.txt"), RealPath.Of(link + "/secret.txt", disk: disk));
        Assert.Equal(Reached.Outside, reached.Type);
    }

    [Fact]
    public void Disk_IsTheMachines_UnlessSet_AndRegisteredWithTheOptions()
    {
        var volume = TestFolder.Create();
        var disk = new FailingDisk();
        var services = new ServiceCollection();

        services.AddSessionRoots(_ => [volume], options => options.Disk = disk);
        using var provider = services.BuildServiceProvider();

        Assert.Same(IRootsDisk.Machine, new SessionRootsOptions().Disk);
        Assert.Same(disk, provider.GetRequiredService<SessionRootsOptions>().Disk);
        Assert.True(provider.GetRequiredService<SessionRoots>().Reach("a.txt").IsAllowed);
    }

    private readonly record struct TaskLabel(string Value);

    private static bool IsTask(string name) =>
        name.Length > 0 && name.All(letter => char.IsAsciiLetterLower(letter) || char.IsAsciiDigit(letter) || letter == '-');

    /// <summary>The machine's disk, with whichever question a test names answered its own way: by throwing, or by changing its mind.</summary>
    private sealed class FailingDisk : IRootsDisk
    {
        public Func<string, bool> IsFile { get; init; } = IRootsDisk.Machine.IsFile;

        public Func<string, bool> IsFolder { get; init; } = IRootsDisk.Machine.IsFolder;

        public Func<string, string?> LinkTarget { get; init; } = IRootsDisk.Machine.LinkTarget;

        bool IRootsDisk.IsFile(string path) => IsFile(path);

        bool IRootsDisk.IsFolder(string path) => IsFolder(path);

        string? IRootsDisk.LinkTarget(string path) => LinkTarget(path);
    }
}
