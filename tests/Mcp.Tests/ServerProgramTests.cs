using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

public sealed class ServerProgramTests
{
    /// <summary>A server under test reads none of the developer's settings: its own home, and a settings file that never exists unless the test names one.</summary>
    [Fact]
    public void AServerUnderTest_GetsItsOwnHome_AndNoSettingsFile_UnlessTheTestNamesOne()
    {
        var isolated = ServerProgram.Variables("Snail.MCP.CodeContext", null);
        var named = ServerProgram.Variables("Snail.MCP.CodeContext", new Dictionary<string, string?> { ["SNAIL_MCP_CODECONTEXT_CONFIG"] = "/tmp/mine.json" });

        Assert.Equal(ServerProgram.Home, isolated["HOME"]);
        Assert.Equal(ServerProgram.NoSettingsFile, isolated["SNAIL_MCP_CODECONTEXT_CONFIG"]);
        Assert.False(File.Exists(ServerProgram.NoSettingsFile));
        Assert.Equal("/tmp/mine.json", named["SNAIL_MCP_CODECONTEXT_CONFIG"]);
        Assert.DoesNotContain(ServerProgram.Variables("Other.Program", null).Keys, key => key.EndsWith("_CONFIG", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_WithoutAnOverride_IsTheDllBesideTheTests_WithOneItIsThatProgram()
    {
        const string variable = "SNAIL_TOOLKIT_MCP_TEST_SERVER";

        Environment.SetEnvironmentVariable(variable, null);
        var (command, arguments) = ServerProgram.Command("Snail.MCP.Demo", variable);

        Assert.Equal("dotnet", command);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Snail.MCP.Demo.dll"), Assert.Single(arguments));

        Environment.SetEnvironmentVariable(variable, "/opt/demo/Snail.MCP.Demo");
        try
        {
            string[] noArguments = [];

            Assert.Equal(("/opt/demo/Snail.MCP.Demo", noArguments), ServerProgram.Command("Snail.MCP.Demo", variable));

            Environment.SetEnvironmentVariable(variable, "/opt/demo/Snail.MCP.Demo.dll");
            var (dotnet, dll) = ServerProgram.Command("Snail.MCP.Demo", variable);

            Assert.Equal("dotnet", dotnet);
            Assert.Equal(["/opt/demo/Snail.MCP.Demo.dll"], dll);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Home_OfAServerUnderTest_IsAFolderOfItsOwn_TheSameForTheWholeRun()
    {
        Assert.True(Directory.Exists(ServerProgram.Home));
        Assert.Contains(ServerProgram.Home, TestFolder.Folders);
        Assert.NotEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ServerProgram.Home);
        Assert.Equal(ServerProgram.Home, ServerProgram.Home);
    }

    /// <summary>
    /// A server that starts, keeps its pipes open and never answers initialize — a native host deadlocked at start — fails the
    /// test after the patience, naming the program, and its process is ended; it does not hold the run until the CI's limit.
    /// </summary>
    [Fact]
    public async Task StartAsync_OfAServerThatNeverAnswers_FailsAfterThePatience_NamingTheProgram()
    {
        const string variable = "SNAIL_SILENT_SERVER";
        var silent = Path.Combine(TestFolder.Create(), "silent.sh");
        File.WriteAllText(silent, "#!/bin/sh\nexec sleep 30\n");
        File.SetUnixFileMode(silent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable(variable, silent);

        try
        {
            var timeout = await Assert.ThrowsAsync<TimeoutException>(() => ServerProgram.StartAsync("Snail.MCP.Silent", variable, null, TimeSpan.FromSeconds(2), CancellationToken.None));

            Assert.Contains("silent.sh", timeout.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>A server that exits before it answers initialize fails the start with its exit code and its stderr as values, where the MCP SDK threw a closed transport that held them in its message.</summary>
    [Fact]
    public async Task StartAsync_OfAServerThatExitsAtStart_GivesItsExitCodeAndStderr()
    {
        var environment = new Dictionary<string, string?> { ["SNAIL_MCP_PROBE_FAIL"] = "the probe was told not to start" };

        var failure = await Assert.ThrowsAsync<ServerExitedException>(() => ServerProgram.StartAsync("Snail.MCP.Probe", "SNAIL_PROBE_SERVER", environment));

        Assert.Equal(3, failure.ExitCode);
        Assert.Equal("the probe was told not to start", failure.Stderr);
        Assert.Contains("exited with 3 before it answered initialize", failure.Message, StringComparison.Ordinal);
        Assert.IsType<ModelContextProtocol.Client.ClientTransportClosedException>(failure.InnerException);
    }

    [Fact]
    public void Text_OfAnAnswerOfOneTextBlock_IsThatText_OfAnyOtherAnswerFails()
    {
        var answer = new CallToolResult { Content = [new TextContentBlock { Text = "done" }] };
        var empty = new CallToolResult { Content = [] };

        Assert.Equal("done", answer.Text());
        Assert.Equal("done", ServerProgram.Text(answer));
        Assert.Throws<InvalidOperationException>(() => empty.Text());
    }
}
