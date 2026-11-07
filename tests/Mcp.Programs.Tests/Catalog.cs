using System.Text.Json.Nodes;

namespace Snail.Sdk.Mcp.Programs.Tests;

/// <summary>A catalog of a few commands, granted under the names of tools <c>git_&lt;command&gt;</c>, as Snail.MCP.Git grants its own.</summary>
internal sealed class Catalog : IProgramCommands
{
    private int _sent;

    public int Sent => _sent;

    public IReadOnlyList<string> Names { get; } = ["count", "fail", "log", "remove_layer", "slow", "status"];

    public string ToolOf(string command) => $"git_{command}";

    public async Task<ProgramAnswer> CallAsync(string command, JsonObject arguments, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _sent);

        switch (command)
        {
            case "slow":
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return ProgramAnswer.Ok(null);
            case "count":
                return ProgramAnswer.Ok(new JsonObject { ["n"] = (arguments["n"]?.GetValue<int>() ?? 0) + 1 });
            case "fail":
                return ProgramAnswer.Failed("the branch is gone", "list the branches first", "NotFound", new JsonObject { ["branch"] = "main" });
            case "log":
                return ProgramAnswer.Ok(new JsonObject { ["history"] = new JsonArray("a1", "b2") });
            default:
                return ProgramAnswer.Ok(new JsonObject { ["clean"] = true, ["given"] = arguments.DeepClone() });
        }
    }
}
