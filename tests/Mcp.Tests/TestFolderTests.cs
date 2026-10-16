using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>Temporary folders a cleaner can prove are leftovers: named by the process that made them and the second it started.</summary>
public sealed partial class TestFolderTests
{
    [GeneratedRegex(@"^s3l-test-(?<pid>[0-9]+)-(?<start>[0-9]+)-[a-z0-9]{8}$")]
    private static partial Regex Convention();

    /// <summary>Test classes run in parallel and ask for a folder in the same instant: sixty-four requests are sixty-four distinct folders, each made and each named for the cleaner.</summary>
    [Fact]
    public void Create_FromSixtyFourThreadsAtOnce_MakesDistinctFoldersNamedForTheCleaner()
    {
        var folders = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 64, _ => folders.Add(TestFolder.Create()));

        Assert.Equal(64, folders.Distinct(StringComparer.Ordinal).Count());
        Assert.All(folders, folder => Assert.True(Directory.Exists(folder), folder));
        Assert.All(folders, folder => Assert.Matches(Convention(), Path.GetFileName(folder)));
    }

    [Fact]
    public void Create_NamesTheFolderByThisProcess_AndTheSecondItStarted()
    {
        var folder = TestFolder.Create();

        var name = Convention().Match(Path.GetFileName(folder));

        Assert.True(name.Success, folder);
        Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), name.Groups["pid"].Value);
        Assert.Equal(TestFolder.Start.ToString(CultureInfo.InvariantCulture), name.Groups["start"].Value);
        Assert.Equal("s3l-test-42-1791922736-abcd1234", TestFolder.Name(42, 1791922736, "abcd1234"));
    }

    [Fact]
    public void Create_MakesAnEmptyFolderDirectlyInTheTemporaryFolder_ANewOneEachTime()
    {
        var first = TestFolder.Create();
        var second = TestFolder.Create();

        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), Path.GetDirectoryName(first));
        Assert.Empty(Directory.EnumerateFileSystemEntries(first));
        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(second));
    }

    /// <summary>The start in the name is the second the system reports, so a cleaner that asks the system matches it to the process.</summary>
    [Fact]
    public void Start_IsTheSecondTheSystemReportsAsThisProcessStart()
    {
        using var process = Process.GetCurrentProcess();
        var started = new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();

        Assert.Equal(started, TestFolder.Start);

        if (OperatingSystem.IsMacOS())
            Assert.Equal(StartAsPsReportsIt(), TestFolder.Start);
    }

    /// <summary>What a test or a server wrote into its folder goes with the folder when the test process exits.</summary>
    [Fact]
    public void Folders_AreDeletedWithEverythingInThem_EvenWhenAlreadyGone()
    {
        var folder = TestFolder.Create();
        Directory.CreateDirectory(Path.Combine(folder, ".s3l-mcp-demo"));
        File.WriteAllText(Path.Combine(folder, ".s3l-mcp-demo", "plans.db"), "written");

        TestFolder.Delete(folder);
        TestFolder.Delete(folder);

        Assert.Contains(folder, TestFolder.Folders);
        Assert.False(Directory.Exists(folder));
    }

    private static long StartAsPsReportsIt()
    {
        var ps = new ProcessStartInfo("ps", ["-o", "lstart=", "-p", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)])
        {
            RedirectStandardOutput = true,
            Environment = { ["LC_ALL"] = "C", ["TZ"] = "UTC" },
        };

        using var running = Process.Start(ps)!;
        var line = running.StandardOutput.ReadToEnd().Trim();
        running.WaitForExit();

        var started = DateTime.ParseExact(line, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AllowInnerWhite | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        return new DateTimeOffset(started).ToUnixTimeSeconds();
    }
}
