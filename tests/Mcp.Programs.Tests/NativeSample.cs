using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Snail.Sdk.Mcp.Programs.Tests;

/// <summary>Native/Programs.Native published with Native AOT for this machine, once for the tests that run it.</summary>
/// <remarks>
/// Published beside the tests' own output, so a second run is incremental (seconds, not the minute of a first ILC pass),
/// with an artifacts path of its own, so the sources' obj folders that a solution build writes are never touched.
/// </remarks>
public sealed class NativeSample
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan RunPatience = TimeSpan.FromMinutes(1);

    private readonly Lazy<string> _executable = new(Publish);

    /// <summary>Runs <paramref name="code"/> in a native process of its own and answers with what it printed: result, log, failure.</summary>
    public JsonObject Run(string code)
    {
        var start = new ProcessStartInfo(_executable.Value) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(code);

        var (exitCode, output, errors) = Wait(start, RunPatience);

        Assert.True(exitCode == 0, $"the native process running `{code}` ended with {exitCode} (134 is SIGABRT): {errors}");

        return JsonNode.Parse(output)!.AsObject();
    }

    private static string Publish()
    {
        var root = Root();
        var project = Path.Combine(root, "tests", "Mcp.Programs.Tests", "Native", "Programs.Native.csproj");
        var native = Path.Combine(AppContext.BaseDirectory, "native");
        var published = Path.Combine(native, "out");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in (string[])["publish", project, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier, "--artifacts-path", Path.Combine(native, "artifacts"), "-o", published])
            start.ArgumentList.Add(argument);

        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        var (exitCode, output, errors) = Wait(start, Patience);

        Assert.True(exitCode == 0, $"the native sample did not publish ({exitCode}):\n{output}{errors}");

        var name = OperatingSystem.IsWindows() ? "Snail.Sdk.Programs.Native.exe" : "Snail.Sdk.Programs.Native";

        return Path.Combine(published, name);
    }

    private static (int ExitCode, string Output, string Errors) Wait(ProcessStartInfo start, TimeSpan patience)
    {
        using var process = Process.Start(start)!;
        var output = new StringBuilder();
        var errors = new StringBuilder();

        process.OutputDataReceived += (_, line) => output.AppendLine(line.Data);
        process.ErrorDataReceived += (_, line) => errors.AppendLine(line.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(patience))
        {
            process.Kill(entireProcessTree: true);

            return (-1, output.ToString(), $"it did not finish in {patience}");
        }

        process.WaitForExit();

        return (process.ExitCode, output.ToString(), errors.ToString());
    }

    /// <summary>The repository's root: the nearest folder above the tests' output that holds Snail.Sdk.slnx.</summary>
    private static string Root()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Snail.Sdk.slnx")))
                return folder.FullName;
        }

        throw new InvalidOperationException($"no Snail.Sdk.slnx above {AppContext.BaseDirectory}");
    }
}

/// <summary>A theory that runs a native binary: skipped, with the reason, where no native toolchain can publish one.</summary>
/// <remarks>
/// Native AOT links with the platform's C toolchain: clang from Xcode on macOS, clang (or gcc) on Linux, the C++ build tools
/// on Windows. Without it a publish fails for reasons that say nothing about the code, so the test is skipped instead;
/// SNAIL_NATIVE_TESTS=0 skips it on purpose.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class NativeTheoryAttribute : TheoryAttribute
{
    private static readonly Lazy<string?> Missing = new(Toolchain);

    public NativeTheoryAttribute()
    {
        if (Missing.Value is { } reason)
            Skip = reason;
    }

    private static string? Toolchain()
    {
        if (Environment.GetEnvironmentVariable("SNAIL_NATIVE_TESTS") == "0")
            return "native tests are turned off by SNAIL_NATIVE_TESTS=0";

        if (OperatingSystem.IsMacOS())
            return Answers("xcrun", "--find", "clang") ? null : "no clang from Xcode (xcrun --find clang) to link a native binary with";

        if (OperatingSystem.IsLinux())
            return Answers("clang", "--version") || Answers("gcc", "--version") ? null : "no clang or gcc to link a native binary with";

        if (OperatingSystem.IsWindows())
        {
            var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");

            return File.Exists(vswhere) ? null : "no Visual Studio C++ build tools to link a native binary with";
        }

        return $"Native AOT is not checked on {RuntimeInformation.OSDescription}";
    }

    private static bool Answers(string tool, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };

            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);

            using var process = Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            return process.WaitForExit(TimeSpan.FromSeconds(30)) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
