using System.ComponentModel;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Discovery;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The catalog of a server's tools, read from the tools themselves.</summary>
public sealed class ToolCatalogTests
{
    [Fact]
    public void Catalog_OfAClass_HasEveryToolWithTheParametersOfItsSchema()
    {
        var entries = ToolCatalog.Of(typeof(FileSample));
        var read = entries.Single(entry => entry.Name == "read_file");

        Assert.Equal(["list_files", "read_file", "write_file", "fail_always", "big_answer", "break_down", "tag_files"], entries.Select(entry => entry.Name));
        Assert.Equal("Reads the text of a file.", read.Description);
        Assert.Equal(new ToolParameter("path", "string", true, "The file's path."), read.Parameters[0]);
        Assert.Equal(("timeoutSeconds", "integer", false), (read.Parameters[1].Name, read.Parameters[1].Type, read.Parameters[1].IsRequired));
    }

    [Fact]
    public void Catalog_OfASkillsClass_NamesTheSkillOfEachTool()
    {
        var entries = ToolCatalog.Of(typeof(RenderSample));

        Assert.All(entries, entry => Assert.Equal("render", entry.Skill));
    }

    /// <summary>A parameter the services fill is the SDK's, not the caller's, and stays out as it does on the wire.</summary>
    [Fact]
    public void Catalog_GivenTheServices_LeavesOutTheParametersTheyFill()
    {
        var services = new ServiceCollection().AddSingleton(new Writes()).BuildServiceProvider();

        var write = ToolCatalog.Of(typeof(FileSample), services).Single(entry => entry.Name == "write_file");

        Assert.Equal(["path", "text"], write.Parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public void Catalog_OfRegisteredTools_NamesTheCommandTheServerGivesEach()
    {
        var services = new ServiceCollection().AddSingleton(new Writes()).BuildServiceProvider();
        var declared = typeof(FileSample).GetMethods().Where(method => method.IsDefined(typeof(McpServerToolAttribute), false));
        var tools = declared.Select(method => McpServerTool.Create(method, target: null, new McpServerToolCreateOptions { Services = services }));

        var entries = ToolCatalog.Of(tools, name => name == "read_file" ? "read" : null);

        Assert.Equal(["read"], entries.Select(entry => entry.Command).OfType<string>());
    }

    /// <summary>"array" told a model nothing about what to put in it; Snail.MCP.CleanUp's own index had written string[].</summary>
    [Theory]
    [InlineData("""{"type":"array","items":{"type":"string"}}""", "string[]")]
    [InlineData("""{"type":["array","null"],"items":{"type":"integer"}}""", "integer[]")]
    [InlineData("""{"type":"array","items":{"type":"array","items":{"type":"number"}}}""", "number[][]")]
    [InlineData("""{"type":"array","items":{"type":"object","properties":{"x":{"type":"integer"}}}}""", "object[]")]
    [InlineData("""{"type":"array","items":{"properties":{"x":{"type":"integer"}}}}""", "object[]")]
    [InlineData("""{"type":"array","items":{"enum":["a","b"]}}""", "string[]")]
    [InlineData("""{"type":"array","items":{}}""", "array")]
    [InlineData("""{"type":"array"}""", "array")]
    [InlineData("""{"enum":["a","b"]}""", "string")]
    [InlineData("""{}""", "object")]
    public void Type_OfAnArray_IsNamedByItsItems(string property, string type)
    {
        var schema = JsonElement.Parse($$"""{"type":"object","properties":{"p": {{property}} } }""");

        Assert.Equal(type, ToolCatalog.ParametersOf(schema)[0].Type);
    }

    [Fact]
    public void Type_OfAnArrayParameter_IsItsItemsAsTheSdkWritesTheSchema()
    {
        var tag = ToolCatalog.Of(typeof(FileSample)).Single(entry => entry.Name == "tag_files");

        Assert.Equal("string[]", tag.Parameters[0].Type);
    }

    /// <summary>Snail.MCP.Git needed a host to test that its words find its tools; the ranking is public over entries.</summary>
    [Fact]
    public void Find_RanksEntriesWithoutAServer_AsFindToolDoes()
    {
        IReadOnlyList<ToolEntry> entries =
        [
            new("git_status", "Shows the state of the working tree.", null, null, []),
            new("git_push", "Sends commits to the remote.", null, null, []),
            new("git_log", "Shows the history of commits.", null, null, []),
        ];
        var options = new DiscoveryOptions { Prefix = "git_" };
        options.Meanings["git_push"] = "publish upload";

        var found = ToolCatalog.Find(entries, "publish my commits", 5, options);

        Assert.Equal(["git_push", "git_log"], found.Select(entry => entry.Name));
        Assert.Equal(["git_log"], ToolCatalog.Find(entries, "git history", 1, options).Select(entry => entry.Name));
        Assert.Empty(ToolCatalog.Find(entries, "the git", 5, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolCatalog.Find(entries, "push", 0));
    }
}

/// <summary>find_tool: which tools answer a task, ranked as the fleet ranks them, as far as the session may call them.</summary>
public sealed class FindToolTests
{
    [Fact]
    public async Task Task_InTheWordsOfATool_FindsItFirst()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var found = await stand.FindAsync(new() { ["task"] = "write a whole file" });

        Assert.True(found["isMatched"]!.GetValue<bool>());
        Assert.Equal("write_file", Names(found)[0]);
        Assert.Null(found["note"]);
    }

    [Fact]
    public async Task Answer_CarriesWhatEachToolTakes()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var found = await stand.FindAsync(new() { ["task"] = "read the text of a file" });
        var path = found["tools"]![0]!["parameters"]![0]!;

        Assert.Equal("read_file", Names(found)[0]);
        Assert.Equal("""{"name":"path","type":"string","isRequired":true,"description":"The file's path."}""", path.ToJsonString(ToolAnswer.Wire));
    }

    [Fact]
    public async Task Meanings_AreWordsAToolIsFoundBy_AsIfInItsName()
    {
        await using var stand = await DiscoveryStand.StartAsync(discovery: options => options.Meanings["write_file"] = "save store");

        var found = await stand.FindAsync(new() { ["task"] = "save it" });

        Assert.Equal("write_file", Names(found)[0]);
    }

    [Fact]
    public async Task Noise_IsDroppedFromTheTask()
    {
        await using var heard = await DiscoveryStand.StartAsync();
        await using var deaf = await DiscoveryStand.StartAsync(discovery: options => options.Noise.Add("scene"));

        var matched = await heard.FindAsync(new() { ["task"] = "scene" });
        var dropped = await deaf.FindAsync(new() { ["task"] = "scene" });

        Assert.True(matched["isMatched"]!.GetValue<bool>());
        Assert.False(dropped["isMatched"]!.GetValue<bool>());
    }

    [Fact]
    public void Prefix_IsDroppedFromTasksAndNames()
    {
        var vocabulary = new Vocabulary([], "git_");

        Assert.Equal(["push"], vocabulary.Asked("git push"));
        Assert.Equal(4.0, vocabulary.Score(["push"], "git_push", null, null));
    }

    [Fact]
    public void Forms_OfAWord_ScoreBelowTheWordAndAboveTheDescription()
    {
        var vocabulary = new Vocabulary([], null);

        Assert.Equal(2.5, vocabulary.Score(["staging"], "git_stage", null, null));
        Assert.Equal(1.0, vocabulary.Score(["index"], "git_stage", null, "Adds paths to the index."));
        Assert.Equal(0.0, vocabulary.Score(vocabulary.Asked("the"), "git_stage", null, "the"));
    }

    /// <summary>
    /// A word of one to three letters is a form of nothing but itself: the "s" of "composition's" answered "slerp", the "z"
    /// of "+z" answered "zzzz" and the "e" and "g" of "e.g." every word they begin (Snail.MCP.AfterEffects, Blender).
    /// </summary>
    [Theory]
    [InlineData("quaternion slerp gimbal")]
    [InlineData("zzzz qqqq")]
    [InlineData("granular entropy")]
    public void Words_NoToolUses_FindNothing_RatherThanAGuess(string task)
    {
        IReadOnlyList<ToolEntry> entries =
        [
            new("comp_settings", "Changes the composition's frame rate, e.g. 25 or 30.", null, null, []),
            new("mesh_extrude", "Extrudes faces along +z by a distance.", null, null, []),
            new("layer_add", "Adds a layer to a composition, i.e. a solid, a null or a shape.", null, null, []),
        ];

        Assert.Empty(ToolCatalog.Find(entries, task, 5));
    }

    [Fact]
    public void Forms_NeedFourLettersOnBothSides_AndTheCommonWordsOfADescriptionSayNothing()
    {
        var vocabulary = new Vocabulary([], null);

        Assert.Equal(0.0, vocabulary.Score(["slerp"], "comp_settings", null, "The composition's rate."));
        Assert.Equal(0.0, vocabulary.Score(["theirs"], "list_files", null, "Lists their files."));
        Assert.Equal(1.0, vocabulary.Score(["rates"], "comp_settings", null, "The composition's rate."));
        Assert.Equal(4.0, vocabulary.Score(["z"], "move_z", null, null));
    }

    [Fact]
    public async Task Task_InWordsNoToolUses_IsNotMatched()
    {
        await using var stand = await DiscoveryStand.StartAsync(skills: false);

        var found = await stand.FindAsync(new() { ["task"] = "zzzz qqqq" });

        Assert.False(found["isMatched"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Task_ThatMatchesNothing_IsAnsweredWithEveryTool_AndANote()
    {
        await using var stand = await DiscoveryStand.StartAsync(skills: false);

        var found = await stand.FindAsync(new() { ["task"] = "teleport a cat" });

        Assert.False(found["isMatched"]!.GetValue<bool>());
        Assert.Contains("nothing in this server's words matches", found["note"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("ask again", found["note"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(stand.Listed.Count, Names(found).Count);
    }

    /// <summary>An empty find_tool answered with every tool's parameters weighed 70 357 characters in Snail.MCP.Roslyn.</summary>
    [Fact]
    public async Task Listing_OfEveryTool_NamesEachWithTheFirstSentence_AndNoParameters()
    {
        await using var stand = await DiscoveryStand.StartAsync(skills: false);

        var found = await stand.FindAsync(new() { ["task"] = "" });
        var find = found["tools"]!.AsArray().Single(tool => tool!["name"]!.GetValue<string>() == "find_tool")!;

        Assert.StartsWith("no task was given", found["note"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.All(found["tools"]!.AsArray(), tool => Assert.Null(tool!["parameters"]));
        Assert.Equal(ToolDescriptions.FindTool[..(ToolDescriptions.FindTool.IndexOf(". ", StringComparison.Ordinal) + 1)], find["description"]!.GetValue<string>());
    }

    /// <summary>In the order of registration, find_tool and run_tools opened Snail.MCP.CleanUp's list before any tool of its own.</summary>
    [Fact]
    public async Task Listing_OfEveryTool_PutsTheToolsOfThePrefixFirst()
    {
        await using var stand = await DiscoveryStand.StartAsync(skills: false, discovery: options => options.Prefix = "render_");

        var found = await stand.FindAsync(new() { ["task"] = "teleport a cat" });

        Assert.Equal("render_image", Names(found)[0]);
        Assert.Equal(["find_tool", "run_tools"], Names(found).Skip(1).Take(2));
    }

    [Theory]
    [InlineData("Reads a file. Then more.", "Reads a file.")]
    [InlineData("Reads a file\nwith more.", "Reads a file")]
    [InlineData("  Reads v1.2 files.  ", "Reads v1.2 files.")]
    public void Summary_IsTheFirstSentence(string description, string summary) =>
        Assert.Equal(summary, Discovery.Summary(description));

    [Fact]
    public void Summary_OfALongSentence_IsCutAtAWord()
    {
        var summary = Discovery.Summary(string.Join(' ', Enumerable.Repeat("word", 60)));

        Assert.EndsWith("word…", summary, StringComparison.Ordinal);
        Assert.True(summary.Length <= 161, summary);
    }

    /// <summary>The SDK's example "push the branch" was Snail.MCP.Git's; a server gives its own words, or none is shown.</summary>
    [Fact]
    public async Task Examples_AreTheServersOwn_AndNoneByDefault()
    {
        await using var plain = await DiscoveryStand.StartAsync(skills: false);
        await using var given = await DiscoveryStand.StartAsync(skills: false, discovery: options => options.Examples = ["rename a symbol", "find unused code"]);

        Assert.Equal("What you are trying to do, in words.", TaskDescription(plain.Listed));
        Assert.Equal("What you are trying to do, in words: 'rename a symbol', 'find unused code'.", TaskDescription(given.Listed));
        Assert.True((await given.FindAsync(new() { ["task"] = "read a file" }))["isMatched"]!.GetValue<bool>());
    }

    /// <summary>The descriptions had grown to 844 and 1028 bytes against the fleet's 518 and 657: every session reads them.</summary>
    [Fact]
    public async Task Listing_OfFindToolAndRunTools_StaysShort()
    {
        await using var stand = await DiscoveryStand.StartAsync(skills: false);

        Assert.InRange(ListedBytes(stand.Listed, "find_tool"), 1, 600);
        Assert.InRange(ListedBytes(stand.Listed, "run_tools"), 1, 680);
    }

    /// <summary>A server whose tools state output schemas (Snail.MCP.FileSystem, every tool) gets find_tool and run_tools stating theirs, in its shape.</summary>
    [Fact]
    public async Task Server_ThatStatesOutputSchemas_GetsTheirsStated_InTheShapeItChose()
    {
        await using var plain = await DiscoveryStand.StartAsync(skills: false);
        await using var data = await DiscoveryStand.StartAsync(skills: false, tools: DiscoveryStand.WithStatedSchema);
        await using var envelope = await DiscoveryStand.StartAsync(
            skills: false,
            discovery: options => options.Shape = AnswerShape.Envelope,
            sequences: options => options.Shape = AnswerShape.Envelope,
            tools: DiscoveryStand.WithStatedSchema);

        var foundData = await data.Client.CallToolAsync("find_tool", new Dictionary<string, object?> { ["task"] = "read a file" });
        var foundEnvelope = await envelope.Client.CallToolAsync("find_tool", new Dictionary<string, object?> { ["task"] = "read a file" });
        var ranEnvelope = await envelope.RunAsync(new JsonObject { ["tool"] = "list_files" });

        Assert.All(plain.Listed.Where(tool => tool.Name is "find_tool" or "run_tools"), tool => Assert.Null(tool.ProtocolTool.OutputSchema));
        Assert.True(Schema(data.Listed, "find_tool").GetProperty("properties").TryGetProperty("isMatched", out _));
        Assert.True(Schema(data.Listed, "run_tools").GetProperty("properties").TryGetProperty("steps", out _));
        Assert.True(foundData.StructuredContent!.Value.TryGetProperty("isMatched", out _));
        Assert.True(Schema(envelope.Listed, "find_tool").GetProperty("properties").GetProperty("data").GetProperty("properties").TryGetProperty("tools", out _));
        Assert.True(foundEnvelope.StructuredContent!.Value.GetProperty("ok").GetBoolean());
        Assert.Equal(1, ranEnvelope.StructuredContent!.Value.GetProperty("data").GetProperty("ran").GetInt32());
    }

    private static string TaskDescription(IList<McpClientTool> listed) =>
        listed.Single(tool => tool.Name == "find_tool").ProtocolTool.InputSchema.GetProperty("properties").GetProperty("task").GetProperty("description").GetString()!;

    private static int ListedBytes(IList<McpClientTool> listed, string name) =>
        Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(listed.Single(tool => tool.Name == name).ProtocolTool, McpJsonUtilities.DefaultOptions));

    private static JsonElement Schema(IList<McpClientTool> listed, string name) => listed.Single(tool => tool.Name == name).ProtocolTool.OutputSchema!.Value;

    /// <summary>Blender's and AfterEffects' find_tool took intent, and their logs show clients sending task: either spelling is answered.</summary>
    [Fact]
    public async Task Intent_IsTakenForTask()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var found = await stand.FindAsync(new() { ["intent"] = "read the text of a file" });

        Assert.Equal("read the text of a file", found["task"]!.GetValue<string>());
        Assert.Equal("read_file", Names(found)[0]);
    }

    [Fact]
    public async Task Tools_TheSessionMayNotCall_AreNotNamed()
    {
        await using var stand = await DiscoveryStand.StartAsync(new FixedAddress(("tool", "find_tool"), ("tool", "read_file")));

        var found = await stand.FindAsync(new() { ["task"] = "teleport a cat" });

        Assert.Equal(["find_tool", "read_file"], Names(found).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Tool_OfASkill_IsFoundUnloaded_WithItsSkill()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var found = await stand.FindAsync(new() { ["task"] = "render a picture" });
        var render = found["tools"]![0]!;

        Assert.Equal("render_image", render["name"]!.GetValue<string>());
        Assert.Equal("render", render["skill"]!.GetValue<string>());
        Assert.False(render["isLoaded"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Enable_LoadsTheSkillsOfTheToolsFound_AndTheyAreListed()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var found = await stand.FindAsync(new() { ["task"] = "render a picture", ["enable"] = true });
        var listed = await stand.Client.ListToolsAsync();

        Assert.Equal(["render"], found["enabledSkills"]!.AsArray().Select(skill => skill!.GetValue<string>()));
        Assert.True(found["tools"]![0]!["isLoaded"]!.GetValue<bool>());
        Assert.Contains(listed, tool => tool.Name == "render_image");
    }

    [Fact]
    public async Task Server_WithoutSkills_OffersNoEnable_AndAnswersNoSkillFields()
    {
        await using var stand = await DiscoveryStand.StartAsync(skills: false);

        var find = (await stand.Client.ListToolsAsync()).Single(tool => tool.Name == "find_tool");
        var found = await stand.FindAsync(new() { ["task"] = "read a file" });

        Assert.False(find.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("enable", out _));
        Assert.Null(found["tools"]![0]!["isLoaded"]);
    }

    private static List<string> Names(JsonNode found) => [.. found["tools"]!.AsArray().Select(tool => tool!["name"]!.GetValue<string>())];
}

/// <summary>run_tools: several tools in one call, held to the grant through ToolForwarding, inside a budget.</summary>
public sealed class RunToolsTests
{
    [Fact]
    public async Task Steps_RunInOrder_EachAnswerUnwrappedFromItsEnvelope()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var ran = await stand.RunAsync(Step("list_files"), Step("read_file", new JsonObject { ["path"] = "a.txt" }));
        var data = ran.Data()!;

        Assert.True(ran.IsOk());
        Assert.Equal(2, data["ran"]!.GetValue<int>());
        Assert.Equal("""{"tool":"list_files","ok":true,"data":{"files":["a.txt","b.txt"]}}""", data["steps"]![0]!.ToJsonString());
        Assert.Equal("text of a.txt", data["steps"]![1]!["data"]!.GetValue<string>());
    }

    [Fact]
    public async Task Run_StopsAtTheFirstStepThatFails_WithItsErrorAndHint()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var ran = await stand.RunAsync(Step("list_files"), Step("fail_always"), Step("write_file", new JsonObject { ["path"] = "a", ["text"] = "x" }));

        Assert.True(ran.IsError);
        Assert.Equal("step 2 of 3 (fail_always) failed: it always fails", ran.Error());
        Assert.Equal("do not call it", ran.Hint());
        Assert.Equal(2, ran.Data()!["ran"]!.GetValue<int>());
        Assert.Equal(0, stand.Writes.Count);
    }

    /// <summary>A role granted run_tools reached every tool of three servers through it; the step is held to the grant by ToolForwarding.</summary>
    [Fact]
    public async Task Step_TheSessionWasNotGranted_IsRefused_AndNeverRuns()
    {
        await using var stand = await DiscoveryStand.StartAsync(new FixedAddress(("tool", "run_tools"), ("tool", "list_files")));

        var ran = await stand.RunAsync(Step("write_file", new JsonObject { ["path"] = "a", ["text"] = "x" }));

        Assert.True(ran.IsError);
        Assert.Contains("'write_file' was not granted to this session", ran.Error(), StringComparison.Ordinal);
        Assert.Equal(0, stand.Writes.Count);
    }

    [Fact]
    public async Task Step_ThatRunsOtherTools_IsRefused()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var ran = await stand.RunAsync(Step("run_tools", new JsonObject { ["steps"] = new JsonArray() }));

        Assert.True(ran.IsError);
        Assert.Contains("'run_tools' runs other tools itself", ran.Error(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_PastTheBudget_IsLeftOut_AndItsStepFlagged()
    {
        await using var stand = await DiscoveryStand.StartAsync(sequences: options => options.OutputBudget = 100);

        var ran = await stand.RunAsync(Step("big_answer"), Step("list_files"));
        var data = ran.Data()!;

        Assert.True(ran.IsOk());
        Assert.True(data["steps"]![0]!["isOmitted"]!.GetValue<bool>());
        Assert.NotNull(data["steps"]![1]!["data"]);
        Assert.Equal(1, data["omitted"]!.GetValue<int>());
        Assert.Contains("call those tools on their own", data["note"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_LongerThanTheLimit_IsRefused()
    {
        await using var stand = await DiscoveryStand.StartAsync(sequences: options => options.MaxSteps = 1);

        var ran = await stand.RunAsync(Step("list_files"), Step("list_files"));

        Assert.Equal("a run takes at most 1 steps, not 2", ran.Error());
    }

    [Fact]
    public async Task Step_NamingNoTool_IsRefused_WithTheNearestName()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var ran = await stand.RunAsync(Step("read_fil"));

        Assert.Equal("step 1 of 1 (read_fil) failed: this server has no tool 'read_fil'", ran.Error());
        Assert.StartsWith("did you mean 'read_file'", ran.Hint(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Step_WithAnArgumentItsToolDoesNotTake_IsRefused_AsADirectCallWouldBe()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var ran = await stand.RunAsync(Step("read_file", new JsonObject { ["pth"] = "a" }));

        Assert.Contains("takes no 'pth' (did you mean 'path'?)", ran.Error(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Step_OfASkillNotLoaded_RunsWithoutLoadingIt()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var ran = await stand.RunAsync(Step("render_image"));
        var listed = await stand.Client.ListToolsAsync();

        Assert.True(ran.IsOk());
        Assert.DoesNotContain(listed, tool => tool.Name == "render_image");
    }

    /// <summary>A step that threw failed the whole run as internal, and the answers of the steps before it were lost (Snail.MCP.Roslyn).</summary>
    [Fact]
    public async Task Step_ThatThrows_FailsTheRun_KeepingWhatTheStepsBeforeItAnswered()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var ran = await stand.RunAsync(Step("list_files"), Step("break_down"), Step("write_file", new JsonObject { ["path"] = "a", ["text"] = "x" }));
        var data = ran.Data()!;

        Assert.True(ran.IsError);
        Assert.Equal("step 2 of 3 (break_down) failed: the disk is gone", ran.Error());
        Assert.Equal("internal", JsonNode.Parse(ran.Text())!["type"]!.GetValue<string>());
        Assert.Equal(2, data["ran"]!.GetValue<int>());
        Assert.NotNull(data["steps"]![0]!["data"]);
        Assert.Equal("internal", data["steps"]![1]!["type"]!.GetValue<string>());
        Assert.Equal(0, stand.Writes.Count);
    }

    [Fact]
    public async Task Step_WhoseArgumentsDoNotBind_FailsAsTheCallsFault()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var ran = await stand.RunAsync(Step("read_file", new JsonObject { ["path"] = "a", ["timeoutSeconds"] = "many" }));

        Assert.True(ran.IsError);
        Assert.Contains("'timeoutSeconds'", ran.Error(), StringComparison.Ordinal);
        Assert.Equal("arguments", JsonNode.Parse(ran.Text())!["type"]!.GetValue<string>());
    }

    /// <summary>run_tools said it reached the world on a server whose every tool is closed (Snail.MCP.FileSystem).</summary>
    [Fact]
    public async Task Hints_AreThoseOfTheToolsItCanRun()
    {
        await using var open = await DiscoveryStand.StartAsync();
        await using var closed = await DiscoveryStand.StartAsync(skills: false, tools: server => server.WithTools<NoteSample>());

        var wide = Annotations(open.Listed);
        var narrow = Annotations(closed.Listed);

        Assert.Equal((false, true, true), (wide.ReadOnlyHint, wide.DestructiveHint, wide.OpenWorldHint));
        Assert.Equal((true, false, false), (narrow.ReadOnlyHint, narrow.DestructiveHint, narrow.OpenWorldHint));
    }

    private static ToolAnnotations Annotations(IList<McpClientTool> listed) => listed.Single(tool => tool.Name == "run_tools").ProtocolTool.Annotations!;

    private static JsonObject Step(string tool, JsonObject? arguments = null) =>
        arguments is null ? new JsonObject { ["tool"] = tool } : new JsonObject { ["tool"] = tool, ["arguments"] = arguments };
}

/// <summary>The argument check: a call that does not fit its tool's schema is refused, naming what it meant.</summary>
/// <remarks>The cases are the ones Snail.MCP.Blender's logs held: each was answered with "An error occurred invoking" and cost a round trip.</remarks>
public sealed class ArgumentCheckTests
{
    [Fact]
    public async Task ShortFormOfAParameter_IsRefused_WithTheParameterAsTheGuess()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var refused = await stand.Client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = "a", ["timeout"] = 5 });

        Assert.True(refused.IsError);
        Assert.Equal("'read_file' takes no 'timeout' (did you mean 'timeoutSeconds'?).", refused.Error());
        Assert.Equal("'read_file' takes path (required), timeoutSeconds.", refused.Hint());
        Assert.Contains("\"type\":\"arguments\"", refused.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRequiredParameter_IsNamed()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var refused = await stand.Client.CallToolAsync("write_file", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.Equal("'write_file' needs 'path', which this call did not send.", refused.Error());
        Assert.Equal(0, stand.Writes.Count);
    }

    [Fact]
    public async Task WordLikeNothingTheToolTakes_IsRefused_WithoutAGuess()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var refused = await stand.Client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = "a", ["wildfire"] = 1 });

        Assert.Equal("'read_file' takes no 'wildfire'.", refused.Error());
    }

    [Fact]
    public async Task CallThatFitsTheSchema_PassesThrough()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var read = await stand.Client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = "a", ["timeoutSeconds"] = 5 });

        Assert.True(read.IsOk());
    }

    [Theory]
    [InlineData("path", "remotePath")]
    [InlineData("timeout", "timeoutSeconds")]
    [InlineData("colour", "color")]
    [InlineData("tsak", "task")]
    public void Nearest_FindsWhatAMistakenNameMeant(string sent, string meant)
    {
        Assert.Equal(meant, ArgumentCheck.Nearest(sent, ["remotePath", "timeoutSeconds", "color", "task", "limit"]).First());
    }

    [Fact]
    public void Nearest_OfAnUnrelatedWord_IsNothing()
    {
        Assert.Empty(ArgumentCheck.Nearest("wildfire", ["intent", "limit", "enable"]));
    }
}

/// <summary>Skills: loaded per session on demand, unloaded when idle.</summary>
public sealed class SkillsTests
{
    [Fact]
    public async Task List_LeavesOutTheToolsOfASkillNotLoaded()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var listed = await stand.Client.ListToolsAsync();

        Assert.DoesNotContain(listed, tool => tool.Name is "render_image" or "bake_texture");
        Assert.Contains(listed, tool => tool.Name == "sample_skills");
        Assert.Contains(listed, tool => tool.Name == "sample_enable_skill");
        Assert.Contains(listed, tool => tool.Name == "sample_disable_skill");
    }

    [Fact]
    public async Task Enable_ListsTheSkillsTools_AndTellsTheClient_Disable_TakesThemAway()
    {
        await using var stand = await DiscoveryStand.StartAsync(protocolVersion: "2025-11-25");

        var enabled = await stand.Client.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "Render" });
        await stand.Changed.WaitAsync(DiscoveryStand.Patience);
        var withSkill = await stand.Client.ListToolsAsync();
        await stand.Client.CallToolAsync("sample_disable_skill", new Dictionary<string, object?> { ["name"] = "render" });
        var without = await stand.Client.ListToolsAsync();

        Assert.True(enabled.Data()!["isLoaded"]!.GetValue<bool>());
        Assert.Equal(["render_image", "bake_texture"], enabled.Data()!["tools"]!.AsArray().Select(tool => tool!.GetValue<string>()));
        Assert.Contains(withSkill, tool => tool.Name == "render_image");
        Assert.DoesNotContain(without, tool => tool.Name == "render_image");
    }

    [Fact]
    public async Task Skills_AreListedWithWhetherEachIsLoaded()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var before = await stand.Client.CallToolAsync("sample_skills");
        await stand.Client.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "render" });
        var after = await stand.Client.CallToolAsync("sample_skills");

        Assert.Equal("""{"skills":[{"name":"render","description":"Rendering pictures of the scene.","isLoaded":false,"tools":["render_image","bake_texture"]}]}""", before.Data()!.ToJsonString());
        Assert.True(after.Data()!["skills"]![0]!["isLoaded"]!.GetValue<bool>());
    }

    [Fact]
    public async Task UnknownSkill_IsRefused_NamingTheSkillsThereAre()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var refused = await stand.Client.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "physics" });

        Assert.True(refused.IsError);
        Assert.Equal("this server has no skill 'physics'", refused.Error());
        Assert.Equal("its skills are render", refused.Hint());
    }

    [Fact]
    public async Task Call_ToAToolOfASkillNotLoaded_LoadsIt()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        var rendered = await stand.Client.CallToolAsync("render_image");
        var listed = await stand.Client.ListToolsAsync();

        Assert.True(rendered.IsOk());
        Assert.Contains(listed, tool => tool.Name == "render_image");
    }

    [Fact]
    public async Task Skill_UnusedForTheIdleTimeout_IsUnloaded_AndTheClientTold()
    {
        await using var stand = await DiscoveryStand.StartAsync(idle: TimeSpan.FromMinutes(10), protocolVersion: "2025-11-25");

        await stand.Client.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "render" });
        await stand.Changed.WaitAsync(DiscoveryStand.Patience);
        stand.ForgetChanges();
        stand.Clock.Advance(TimeSpan.FromMinutes(11));
        stand.Expiry.Sweep();
        await stand.Changed.WaitAsync(DiscoveryStand.Patience);
        var listed = await stand.Client.ListToolsAsync();

        Assert.DoesNotContain(listed, tool => tool.Name == "render_image");
    }

    [Fact]
    public async Task Skill_InUse_StaysLoadedPastTheIdleTimeout()
    {
        await using var stand = await DiscoveryStand.StartAsync(idle: TimeSpan.FromMinutes(10));

        await stand.Client.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "render" });
        stand.Clock.Advance(TimeSpan.FromMinutes(6));
        await stand.Client.CallToolAsync("render_image");
        stand.Clock.Advance(TimeSpan.FromMinutes(6));
        stand.Expiry.Sweep();
        var listed = await stand.Client.ListToolsAsync();

        Assert.Contains(listed, tool => tool.Name == "render_image");
    }

    [Fact]
    public async Task Server_SaysItTellsOfAChangeToItsTools()
    {
        await using var stand = await DiscoveryStand.StartAsync();

        Assert.True(stand.Client.ServerCapabilities.Tools?.ListChanged);
    }

    [Fact]
    public async Task ToolsDiscoveryAdds_KeepTheGuardRules()
    {
        await using var stand = await DiscoveryStand.StartAsync();
        string[] added = ["find_tool", "run_tools", "sample_skills", "sample_enable_skill", "sample_disable_skill"];

        var listed = await stand.Client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Where(tool => added.Contains(tool.Name)).Select(tool => tool.ProtocolTool)));
    }
}

/// <summary>A server of sample tools with discovery, sequences, the argument check and skills, over a pair of pipes.</summary>
internal sealed class DiscoveryStand : IAsyncDisposable
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly IAsyncDisposable _listening;

    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DiscoveryStand(IHost host, McpClient client, Writes writes, ManualClock clock, IList<McpClientTool> listed)
    {
        Host = host;
        Client = client;
        Writes = writes;
        Clock = clock;
        Listed = listed;
        _listening = client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification, (_, _) =>
        {
            _changed.TrySetResult();

            return ValueTask.CompletedTask;
        });
    }

    public IHost Host { get; }

    public McpClient Client { get; }

    public Writes Writes { get; }

    public ManualClock Clock { get; }

    /// <summary>The tools listed when the client connected.</summary>
    public IList<McpClientTool> Listed { get; }

    /// <summary>Completes when the client is told its tool list changed.</summary>
    public Task Changed => _changed.Task;

    internal SkillExpiry Expiry => Host.Services.GetServices<IHostedService>().OfType<SkillExpiry>().Single();

    public static async Task<DiscoveryStand> StartAsync(
        ISessionAddress? address = null,
        bool skills = true,
        Action<DiscoveryOptions>? discovery = null,
        Action<SequenceOptions>? sequences = null,
        TimeSpan idle = default,
        string? protocolVersion = null,
        Action<IMcpServerBuilder>? tools = null)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var writes = new Writes();
        var clock = new ManualClock();
        var builder = McpHost.CreateBuilder([]);

        builder.Services.AddSingleton(writes);
        builder.Services.AddSingleton<TimeProvider>(clock);

        if (address is not null)
            builder.Services.AddSingleton(address);

        var server = builder.Services.AddMcpServer()
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream())
            .RefuseWhatWasNotGranted()
            .WithArgumentCheck()
            .WithToolDiscovery(discovery)
            .WithToolSequences(sequences);

        (tools ?? WithSamples)(server);

        if (skills)
            server.WithSkills(options => (options.Name, options.IdleTimeout) = ("sample", idle));

        var host = builder.Build();
        await host.StartAsync();
        var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), new McpClientOptions { ProtocolVersion = protocolVersion });
        var listed = await client.ListToolsAsync();

        return new DiscoveryStand(host, client, writes, clock, listed);
    }

    /// <summary>The sample tools every stand has unless a test gives its own.</summary>
    public static void WithSamples(IMcpServerBuilder server) => server.WithTools<FileSample>().WithTools<RenderSample>();

    /// <summary>The samples and a tool that states an output schema, as every tool of Snail.MCP.FileSystem does.</summary>
    public static void WithStatedSchema(IMcpServerBuilder server)
    {
        var options = new McpServerToolCreateOptions
        {
            Name = "stat_file",
            Description = "States the size of a file.",
            ReadOnly = true,
            OpenWorld = false,
            UseStructuredContent = true,
            OutputSchema = JsonElement.Parse("""{"type":"object","properties":{"size":{"type":"integer"}},"required":["size"]}"""),
        };

        WithSamples(server);
        server.Services.AddSingleton(McpServerTool.Create(() => ToolAnswer.Ok(new JsonObject { ["size"] = 1 }), options));
    }

    /// <summary>The data of find_tool's answer to these arguments.</summary>
    public async Task<JsonNode> FindAsync(Dictionary<string, object?> arguments)
    {
        var found = await Client.CallToolAsync("find_tool", arguments);

        Assert.True(found.IsOk(), found.Text());

        return found.Data()!;
    }

    public async Task<CallToolResult> RunAsync(params JsonObject[] steps) =>
        await Client.CallToolAsync("run_tools", new Dictionary<string, object?> { ["steps"] = new JsonArray([.. steps]) });

    /// <summary>Waits for the next change rather than one already told.</summary>
    public void ForgetChanges() => _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        await _listening.DisposeAsync();
        await Client.DisposeAsync();
        await Host.StopAsync();
        Host.Dispose();
    }
}

/// <summary>A clock a test moves by hand.</summary>
internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>What the sample tools wrote, for a test to see.</summary>
internal sealed class Writes
{
    private int _count;

    public int Count => _count;

    public void Add() => Interlocked.Increment(ref _count);
}

[McpServerToolType]
internal sealed class FileSample
{
    [McpServerTool(Name = "list_files", ReadOnly = true), Description("Lists the files of the folder.")]
    public static CallToolResult ListFiles() => ToolAnswer.Ok(new JsonObject { ["files"] = new JsonArray("a.txt", "b.txt") });

    [McpServerTool(Name = "read_file", ReadOnly = true), Description("Reads the text of a file.")]
    public static CallToolResult ReadFile([Description("The file's path.")] string path, [Description("How long to wait.")] int timeoutSeconds = 30) =>
        ToolAnswer.Ok(JsonValue.Create($"text of {path}"));

    [McpServerTool(Name = "write_file", Destructive = true), Description("Writes a whole file.")]
    public static CallToolResult WriteFile(Writes writes, [Description("The file's path.")] string path, [Description("What to write.")] string text)
    {
        writes.Add();

        return ToolAnswer.Ok(new JsonObject { ["written"] = path });
    }

    [McpServerTool(Name = "fail_always", ReadOnly = true), Description("Fails.")]
    public static CallToolResult FailAlways() => ToolAnswer.Failed("it always fails", "do not call it");

    [McpServerTool(Name = "big_answer", ReadOnly = true), Description("Answers at length.")]
    public static CallToolResult BigAnswer() => ToolAnswer.Ok(JsonValue.Create(new string('x', 500)));

    [McpServerTool(Name = "break_down", ReadOnly = true), Description("Breaks down inside.")]
    public static CallToolResult BreakDown() => throw new InvalidOperationException("the disk is gone");

    [McpServerTool(Name = "tag_files", Destructive = false), Description("Tags files.")]
    public static CallToolResult TagFiles([Description("The files' paths.")] string[] paths) => ToolAnswer.Ok(new JsonObject { ["tagged"] = paths.Length });
}

[McpServerToolType]
internal sealed class NoteSample
{
    [McpServerTool(Name = "peek_note", ReadOnly = true, OpenWorld = false), Description("Shows a note.")]
    public static CallToolResult PeekNote() => ToolAnswer.Ok(new JsonObject { ["note"] = "n" });

    [McpServerTool(Name = "count_notes", ReadOnly = true, OpenWorld = false), Description("Counts the notes.")]
    public static CallToolResult CountNotes() => ToolAnswer.Ok(new JsonObject { ["count"] = 1 });
}

[McpServerToolType]
[Skill("render", "Rendering pictures of the scene.")]
internal sealed class RenderSample
{
    [McpServerTool(Name = "render_image", ReadOnly = true), Description("Renders a picture of the scene.")]
    public static CallToolResult RenderImage([Description("Width in pixels.")] int width = 640) => ToolAnswer.Ok(new JsonObject { ["width"] = width });

    [McpServerTool(Name = "bake_texture", Destructive = false), Description("Bakes the texture of a material.")]
    public static CallToolResult BakeTexture() => ToolAnswer.Ok(new JsonObject { ["baked"] = true });
}
