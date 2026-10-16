using System.Collections.Concurrent;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The folders of a server under its s3l name, and the move from its snail name.</summary>
public sealed class ServerFoldersTests : IDisposable
{
    private readonly string _home = TestFolder.Create();
    private readonly StringWriter _warnings = new();

    /// <summary>Naming a folder moves nothing: a test that builds a server's settings with the real home leaves it as it was.</summary>
    [Fact]
    public void DataDirectory_WhileOnlyTheOldOneExists_IsTheOldOne_AndNothingMoves()
    {
        var former = Directory.CreateDirectory(Path.Combine(_home, ".snail-mcp-demo")).FullName;

        var data = ServerFolders.DataDirectory("demo", _home);

        Assert.Equal(former, data);
        Assert.False(Directory.Exists(Path.Combine(_home, ".s3l-mcp-demo")));
        Assert.Null(new DirectoryInfo(former).LinkTarget);
    }

    /// <summary>A server that kept its data under the snail name finds it under the new one, and the old name still leads there.</summary>
    [Fact]
    public void MoveFromSnailNames_MovesBothFoldersAndLinksTheOldNames()
    {
        var formerData = Directory.CreateDirectory(Path.Combine(_home, ".snail-mcp-demo")).FullName;
        var formerConfig = Directory.CreateDirectory(Path.Combine(_home, ".config", "snail-mcp-demo")).FullName;
        File.WriteAllText(Path.Combine(formerData, "plans.db"), "kept");
        File.WriteAllText(Path.Combine(formerConfig, "config.json"), "{}");

        ServerFolders.MoveFromSnailNames("demo", _home, _warnings);
        var data = ServerFolders.DataDirectory("demo", _home);
        var config = ServerFolders.ConfigDirectory("demo", _home);

        Assert.Equal(Path.Combine(_home, ".s3l-mcp-demo"), data);
        Assert.Equal(Path.Combine(_home, ".config", "s3l-mcp-demo"), config);
        Assert.Equal("kept", File.ReadAllText(Path.Combine(data, "plans.db")));
        Assert.True(File.Exists(Path.Combine(config, "config.json")));
        Assert.NotNull(new DirectoryInfo(formerData).LinkTarget);
        Assert.Equal("kept", File.ReadAllText(Path.Combine(formerData, "plans.db")));
        Assert.NotNull(new DirectoryInfo(formerConfig).LinkTarget);
    }

    /// <summary>Moved once, the folders stay put: a second start changes nothing.</summary>
    [Fact]
    public void MoveFromSnailNames_Twice_MovesOnce()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".snail-mcp-demo"));

        ServerFolders.MoveFromSnailNames("demo", _home, _warnings);
        ServerFolders.MoveFromSnailNames("demo", _home, _warnings);

        Assert.Equal(Path.Combine(_home, ".s3l-mcp-demo"), ServerFolders.DataDirectory("demo", _home));
    }

    /// <summary>When both names exist the new one is the server's, and the old one, empty, is left as it is without a word.</summary>
    [Fact]
    public void MoveFromSnailNames_WhenBothExist_LeavesTheOldOneAsItIs()
    {
        var former = Directory.CreateDirectory(Path.Combine(_home, ".snail-mcp-demo")).FullName;
        Directory.CreateDirectory(Path.Combine(_home, ".s3l-mcp-demo"));

        ServerFolders.MoveFromSnailNames("demo", _home, _warnings);

        Assert.Equal(Path.Combine(_home, ".s3l-mcp-demo"), ServerFolders.DataDirectory("demo", _home));
        Assert.Null(new DirectoryInfo(former).LinkTarget);
        Assert.Empty(_warnings.ToString());
    }

    /// <summary>Data left under the old name while the new one is in use is named in one line, so its owner can decide.</summary>
    [Fact]
    public void MoveFromSnailNames_WhenBothExistAndTheOldOneHoldsData_WarnsNamingBoth()
    {
        var former = Directory.CreateDirectory(Path.Combine(_home, ".snail-mcp-demo")).FullName;
        var current = Directory.CreateDirectory(Path.Combine(_home, ".s3l-mcp-demo")).FullName;
        File.WriteAllText(Path.Combine(former, "plans.db"), "kept");

        ServerFolders.MoveFromSnailNames("demo", _home, _warnings);

        var warning = Assert.Single(_warnings.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(former, warning, StringComparison.Ordinal);
        Assert.Contains(current, warning, StringComparison.Ordinal);
        Assert.Equal("kept", File.ReadAllText(Path.Combine(former, "plans.db")));
    }

    /// <summary>A link that cannot be made after the move takes the folder back: nothing is lost and the old name still works.</summary>
    [Fact]
    public void TryMove_WhenTheLinkFails_MovesTheFolderBack()
    {
        var former = Directory.CreateDirectory(Path.Combine(_home, ".snail-mcp-demo")).FullName;
        var current = Path.Combine(_home, ".s3l-mcp-demo");
        File.WriteAllText(Path.Combine(former, "plans.db"), "kept");

        var moved = ServerFolders.TryMove(former, current, (_, _) => throw new IOException("links are refused here"), _warnings);

        Assert.False(moved);
        Assert.False(Directory.Exists(current));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(former, "plans.db")));
        Assert.Equal(former, ServerFolders.DataDirectory("demo", _home));
    }

    /// <summary>A server that never ran has nothing to move: its folder is named, not made.</summary>
    [Fact]
    public void DataDirectory_OfANewServer_IsNamedNotMade()
    {
        ServerFolders.MoveFromSnailNames("demo", _home, _warnings);
        var data = ServerFolders.DataDirectory("demo", _home);

        Assert.Equal(Path.Combine(_home, ".s3l-mcp-demo"), data);
        Assert.False(Directory.Exists(data));
    }

    /// <summary>Only an installed program moves folders: one run from a build's output — a test's server — never does.</summary>
    [Theory]
    [InlineData("/Users/me/.s3l-mcp/Snail.MCP.Plans/2026-10-12-2300.abc1234/", true)]
    [InlineData("/Users/me/.dotnet/tools/.store/snail.mcp.plans/0.2.0/snail.mcp.plans/0.2.0/tools/net10.0/any/", true)]
    [InlineData("/Users/me/Projects/Snail.MCP.Plans/src/bin/Release/net10.0/", false)]
    [InlineData("/Users/me/.s3l-mcp-plans/", false)]
    public void IsInstalled_OnlyForAPublishedOrToolProgram(string programFolder, bool isInstalled) =>
        Assert.Equal(isInstalled, ServerFolders.IsInstalled(programFolder, "/Users/me"));

    /// <summary>Sixteen sessions start the same server at once after an upgrade: the data moves once, every file is kept, and the old name leads to it.</summary>
    [Fact]
    public void TryMove_FromSixteenProcessesAtOnce_MovesOnceAndKeepsEveryFile()
    {
        var (former, current, _) = Raced();

        Assert.Equal(50, Directory.EnumerateFiles(current).Count());
        Assert.Equal(current, new DirectoryInfo(former).LinkTarget);
        Assert.Equal(50, Directory.EnumerateFiles(former).Count());
    }

    /// <summary>A process that finds the folders moved a moment before by another has nothing to warn about: a warning that fires on a race teaches people to ignore warnings.</summary>
    [Fact]
    public void TryMove_FromSixteenProcessesAtOnce_WarnsNobody()
    {
        var (_, _, warnings) = Raced();

        Assert.Equal(string.Empty, warnings);
    }

    private static (string Former, string Current, string Warnings) Raced()
    {
        var home = TestFolder.Create();
        var former = Directory.CreateDirectory(Path.Combine(home, ".snail-mcp-demo")).FullName;
        var current = Path.Combine(home, ".s3l-mcp-demo");
        using var warnings = new StringWriter();
        var synchronized = TextWriter.Synchronized(warnings);
        var outcomes = new ConcurrentBag<bool>();

        for (var file = 0; file < 50; file++)
            File.WriteAllText(Path.Combine(former, $"{file}.db"), "kept");

        Parallel.For(0, 16, _ => outcomes.Add(ServerFolders.TryMove(former, current, (link, target) => Directory.CreateSymbolicLink(link, target), synchronized)));

        return (former, current, warnings.ToString());
    }

    public void Dispose()
    {
        _warnings.Dispose();
        Directory.Delete(_home, recursive: true);
    }
}
