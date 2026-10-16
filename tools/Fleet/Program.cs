using System.Diagnostics;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;

// Starts every stdio server of ~/.claude.json as Claude Code would, one at a time, and measures what a session pays.
// Usage: fleet <label> <output.json> [server=command ...]  — with commands given, only those servers, started that way;
// a server the configuration does not name is started with no variables of its own
var (label, output) = (args[0], args[1]);
var overrides = args.Skip(2).Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);
var probes = new Dictionary<string, (string Tool, Dictionary<string, object?> Arguments)>
{
    ["clean"] = ("cleanup_diagnose", []),
    ["mem2"] = ("list_active_plans", []),
    ["mem1"] = ("vault_status", []),
    ["mem0"] = ("list_projects", []),
    ["roslyn"] = ("health_check", []),
    ["codecontext"] = ("get_guidelines", []),
    ["csharp-tools-figma"] = ("figma_get_status", []),
};
var config = JsonNode.Parse(File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json")))!;
var results = new JsonArray();

var configured = config["mcpServers"]!.AsObject();
var servers = configured.Select(pair => (Name: pair.Key, Server: pair.Value!)).ToList();

servers.AddRange(overrides.Keys.Where(name => !configured.ContainsKey(name)).Select(name => (name, (JsonNode)new JsonObject { ["command"] = overrides[name] })));

foreach (var (name, server) in servers)
{
    if (server!["command"] is null || (overrides.Count > 0 && !overrides.ContainsKey(name)))
        continue;

    var command = server["command"]!.GetValue<string>();
    var arguments = server["args"]?.AsArray().Select(argument => argument!.GetValue<string>()).ToList() ?? [];

    if (overrides.TryGetValue(name, out var replaced))
        (command, arguments) = replaced.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ("dotnet", new List<string> { replaced }) : (replaced, new List<string>());
    var environment = server["env"]?.AsObject().ToDictionary(pair => pair.Key, pair => (string?)pair.Value!.GetValue<string>()) ?? [];
    var row = new JsonObject { ["server"] = name, ["command"] = string.Join(' ', [command, .. arguments]) };

    try
    {
        var timer = Stopwatch.StartNew();
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions { Command = command, Arguments = arguments, EnvironmentVariables = environment }));
        row["initializeMs"] = timer.ElapsedMilliseconds;

        var pid = Descendant(Environment.ProcessId, command, arguments);
        row["afterStartMB"] = Resident(pid) / 1024;

        timer.Restart();
        row["tools"] = (await client.ListToolsAsync()).Count;
        row["listMs"] = timer.ElapsedMilliseconds;

        if (probes.TryGetValue(name, out var probe))
        {
            timer.Restart();
            var answer = await client.CallToolAsync(probe.Tool, probe.Arguments);
            row["probe"] = probe.Tool;
            row["probeMs"] = timer.ElapsedMilliseconds;
            row["probeError"] = answer.IsError == true;
        }

        await Task.Delay(TimeSpan.FromSeconds(10));
        row["afterIdleMB"] = Resident(pid) / 1024;
        row["threads"] = Threads(pid);
    }
    catch (Exception failure)
    {
        row["failure"] = failure.Message;
    }

    Console.WriteLine(row.ToJsonString());
    results.Add(row);
}

var total = results.Sum(row => row!["afterIdleMB"]?.GetValue<long>() ?? 0);
var document = new JsonObject { ["label"] = label, ["at"] = DateTimeOffset.Now.ToString("u"), ["sessionIdleMB"] = total, ["servers"] = results };
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, document.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"one session's stdio servers at rest: {total} MB");

static int Descendant(int parent, string command, List<string> arguments)
{
    var needle = arguments.Count > 0 ? arguments[^1] : command;
    var table = Ps("-axo", "pid=,ppid=,command=").Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries)).Where(words => words.Length == 3)
        .Select(words => (Pid: int.Parse(words[0]), Parent: int.Parse(words[1]), Command: words[2])).ToList();
    var mine = new HashSet<int> { parent };
    bool grew;

    do
    {
        grew = false;

        foreach (var row in table)
        {
            if (mine.Contains(row.Parent) && mine.Add(row.Pid))
                grew = true;
        }
    }
    while (grew);

    var matching = table.Where(row => mine.Contains(row.Pid) && row.Command.Contains(needle, StringComparison.Ordinal));

    return matching.Select(row => row.Pid).DefaultIfEmpty(0).Max();
}

static long Resident(int pid) => long.TryParse(Ps("-o", "rss=", "-p", $"{pid}").Trim(), out var kb) ? kb : 0;

static int Threads(int pid) => Ps("-M", "-p", $"{pid}").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;

static string Ps(params string[] arguments)
{
    using var ps = Process.Start(new ProcessStartInfo("ps", arguments) { RedirectStandardOutput = true })!;
    var text = ps.StandardOutput.ReadToEnd();

    ps.WaitForExit();

    return text;
}
