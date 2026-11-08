using System.Text.Json.Nodes;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Programs;

// The program to run is the first argument; the answer is one line of JSON on stdout: result, log, failure.
var runner = new ProgramRunner(new ProgramOptions());
var run = await runner.RunAsync(new Echo(), args[0]);
var log = new JsonArray();

foreach (var line in run.Log)
    log.Add((JsonNode?)JsonValue.Create(line));

var answer = new JsonObject
{
    ["result"] = run.Result?.DeepClone(),
    ["log"] = log,
    ["failure"] = run.Failure?.Error,
};

Console.WriteLine(answer.ToJsonString());

return 0;

/// <summary>One command, <c>tools.echo({ … })</c>, answering with the object it was given.</summary>
internal sealed class Echo : IProgramCommands
{
    public IReadOnlyList<string> Names { get; } = ["echo"];

    public string ToolOf(string command) => command;

    public Task<Answer<JsonNode?>> CallAsync(CommandCall call, CancellationToken cancellationToken) =>
        Task.FromResult(Answer.Ok<JsonNode?>(call.Arguments.DeepClone()));
}
