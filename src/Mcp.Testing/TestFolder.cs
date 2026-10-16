using System.Collections.Concurrent;
using System.Diagnostics;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>Temporary folders of a test, named so that a cleaner can prove a folder is a leftover once its test process has ended.</summary>
/// <remarks>
/// A folder named at random says nothing about who made it: after a run that crashed or was killed, nobody can tell a
/// leftover from a folder still in use, and the temporary folder fills with them. A folder made here is named
/// <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c> directly in <see cref="Path.GetTempPath"/>: the id of the
/// test process and the second it started, in Unix seconds, the same second the system reports as the process's start.
/// When no process with that id that started in that second runs any more, the folder is a leftover, and an id the
/// system gave to another process since does not make it look alive. A folder made here is also deleted, with
/// everything in it, when the test process exits normally.
/// </remarks>
public static class TestFolder
{
    private const string Prefix = "s3l-test-";

    private static readonly Lazy<long> OwnStart = new(StartOfThisProcess);
    private static readonly Lazy<ConcurrentQueue<string>> OwnFolders = new(FoldersDeletedAtExit);

    /// <summary>A new empty folder for a test, directly in the temporary folder and named by the convention a cleaner reads.</summary>
    public static string Create()
    {
        string folder;
        do
        {
            folder = Path.Combine(Path.GetTempPath(), Name(Environment.ProcessId, Start, Path.GetRandomFileName()[..8]));
        }
        while (Directory.Exists(folder));

        OwnFolders.Value.Enqueue(folder);
        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>The second this process started, in Unix seconds, as the system reports it.</summary>
    internal static long Start => OwnStart.Value;

    /// <summary>The folders this process made, each deleted when it exits.</summary>
    internal static IReadOnlyCollection<string> Folders => OwnFolders.Value;

    /// <summary><c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>: the name a cleaner reads the creator from.</summary>
    internal static string Name(int processId, long start, string random) => $"{Prefix}{processId}-{start}-{random}";

    /// <summary>Deletes a folder and everything in it, leaving what cannot be deleted where it is.</summary>
    internal static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception kept) when (kept is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }

    private static long StartOfThisProcess()
    {
        using var process = Process.GetCurrentProcess();

        return new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
    }

    private static ConcurrentQueue<string> FoldersDeletedAtExit()
    {
        var folders = new ConcurrentQueue<string>();

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var folder in folders)
                Delete(folder);
        };

        return folders;
    }
}
