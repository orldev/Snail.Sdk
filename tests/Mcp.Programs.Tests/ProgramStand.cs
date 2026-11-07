using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Programs.Tests;

/// <summary>A server of notes over a pair of pipes, its sessions at a fixed address, with run_program, and the client connected to it.</summary>
internal sealed class ProgramStand : IAsyncDisposable
{
    private ProgramStand(IHost host, McpClient client, Notes notes)
    {
        Host = host;
        Client = client;
        Notes = notes;
    }

    public IHost Host { get; }

    public McpClient Client { get; }

    public Notes Notes { get; }

    /// <summary>A server whose programs call <paramref name="programs"/>' commands, its sessions at <paramref name="address"/>.</summary>
    public static async Task<ProgramStand> StartAsync(ISessionAddress address, Func<IMcpServerBuilder, IMcpServerBuilder> programs)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var notes = new Notes();
        var builder = McpHost.CreateBuilder([]);

        builder.Services.AddSingleton(notes);
        builder.Services.AddSingleton(address);

        var server = builder.Services.AddMcpServer()
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream())
            .RefuseWhatWasNotGranted()
            .ExplainFailures()
            .WithTools<NoteTools>();

        programs(server);

        var host = builder.Build();
        await host.StartAsync();
        var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));

        return new ProgramStand(host, client, notes);
    }

    public ValueTask<CallToolResult> RunAsync(string code, int? timeoutSeconds = null)
    {
        var arguments = new Dictionary<string, object?> { ["code"] = code };

        if (timeoutSeconds is { } seconds)
            arguments["timeoutSeconds"] = seconds;

        return Client.CallToolAsync("run_program", arguments);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Host.StopAsync();
        Host.Dispose();
    }
}

/// <summary>What the sample server's tools changed, for a test to see.</summary>
internal sealed class Notes
{
    private readonly List<string> _written = [];

    public IReadOnlyList<string> Written
    {
        get
        {
            lock (_written)
                return [.. _written];
        }
    }

    public void Write(string text)
    {
        lock (_written)
            _written.Add(text);
    }
}

[McpServerToolType]
internal sealed class NoteTools
{
    [McpServerTool(Name = "notes_read", ReadOnly = true), Description("Reads the notes.")]
    public static CallToolResult Read(Notes notes) =>
        ToolAnswer.Ok(new JsonObject { ["notes"] = new JsonArray([.. notes.Written.Select(text => (JsonNode?)JsonValue.Create(text))]) });

    [McpServerTool(Name = "notes_write", Destructive = true), Description("Writes a note.")]
    public static CallToolResult Write(Notes notes, [Description("What to write.")] string text)
    {
        if (text.Length == 0)
            return ToolAnswer.Failed("a note needs text", "write something", "empty");

        notes.Write(text);

        return ToolAnswer.Ok(new JsonObject { ["written"] = text });
    }

    [McpServerTool(Name = "find_tool", ReadOnly = true), Description("Names the tool that does a thing.")]
    public static string FindTool([Description("The thing.")] string task) => $"notes_read does {task}";

    [McpServerTool(Name = "run_tools", Destructive = true), Description("Runs tools of this server."), Forwards]
    public static string RunTools() => "ran";
}

/// <summary>A catalog of the server's own: <c>read</c> and <c>write</c>, granted under <c>notes_read</c> and <c>notes_write</c>.</summary>
internal sealed class NoteCommands(Notes notes) : IProgramCommands
{
    public IReadOnlyList<string> Names { get; } = ["read", "write"];

    public string ToolOf(string command) => $"notes_{command}";

    public Task<ProgramAnswer> CallAsync(string command, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (command == "write")
            notes.Write(arguments["text"]?.GetValue<string>() ?? string.Empty);

        return Task.FromResult(ProgramAnswer.Ok(new JsonObject { ["count"] = notes.Written.Count }));
    }
}
