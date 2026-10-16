using System.Diagnostics;

namespace Snail.Sdk.Sdk.Tests;

/// <summary>
/// A temporary folder of these tests, named as the standard asks: <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c>
/// directly in the temporary folder, so a cleaner can prove a folder a killed run left behind is a leftover.
/// </summary>
/// <remarks>
/// Snail.Sdk.Mcp.Testing gives a server's tests <c>TestFolder</c>; these tests do not reference it, and name their
/// folders by the same convention, as the standard says of a library's tests. Each test class deletes its own folder.
/// </remarks>
internal static class TestRoot
{
    private static readonly Lazy<long> Start = new(StartOfThisProcess);

    /// <summary>A new empty folder for a test class.</summary>
    public static string Create()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"s3l-test-{Environment.ProcessId}-{Start.Value}-{Path.GetRandomFileName()[..8]}");

        return Directory.CreateDirectory(folder).FullName;
    }

    private static long StartOfThisProcess()
    {
        using var process = Process.GetCurrentProcess();

        return new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
    }
}
