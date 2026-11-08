using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The envelope every tool answers with: its text to the character, its structured content, its failures flagged.</summary>
public sealed class ToolAnswerTests
{
    [Fact]
    public void Ok_WritesTheEnvelope_CompactAndWithNothingEscapedThatJsonAllows()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["line"] = "List<int> a = b & c + 'd' // Привет \"e\"" });

        Assert.Equal("""{"ok":true,"data":{"line":"List<int> a = b & c + 'd' // Привет \"e\""}}""", answer.Text());
        Assert.NotEqual(true, answer.IsError);
    }

    [Fact]
    public void Failed_LeavesOutEveryFieldWithoutAValue()
    {
        var answer = ToolAnswer.Failed("No such file.");

        Assert.Equal("""{"ok":false,"error":"No such file."}""", answer.Text());
    }

    [Fact]
    public void Failed_WritesEveryFieldGiven_OkFirst()
    {
        var answer = ToolAnswer.Failed("Two of three failed.", "Retry the two.", "partial", new JsonObject { ["done"] = 1 });

        Assert.Equal("""{"ok":false,"error":"Two of three failed.","hint":"Retry the two.","type":"partial","data":{"done":1}}""", answer.Text());
    }

    [Fact]
    public void Failed_IsAlwaysAnError_AndCarriesNoStructuredContent()
    {
        var answer = ToolAnswer.Failed("No.", data: new JsonObject { ["done"] = 1 });

        Assert.True(answer.IsError);
        Assert.Null(answer.StructuredContent);
    }

    [Fact]
    public void Ok_ByDefault_CarriesTheDataAsStructuredContent()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["count"] = 2 });

        Assert.Equal("""{"count":2}""", answer.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public void Ok_ShapedAsTheEnvelope_CarriesTheTextsObjectAsStructuredContent()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["count"] = 2 }, AnswerShape.Envelope);

        Assert.Equal(answer.Text(), answer.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public void Ok_ShapedAsNone_CarriesTextAlone()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["count"] = 2 }, AnswerShape.None);

        Assert.Null(answer.StructuredContent);
    }

    /// <summary>The protocol allows only an object as structured content.</summary>
    [Fact]
    public void Ok_OfDataThatIsNoObject_CarriesTextAlone()
    {
        var answer = ToolAnswer.Ok(new JsonArray(1, 2));

        Assert.Equal("""{"ok":true,"data":[1,2]}""", answer.Text());
        Assert.Null(answer.StructuredContent);
    }

    [Fact]
    public void Ok_OfANodeThatHasAParent_LeavesItWhereItIs()
    {
        var whole = new JsonObject { ["part"] = new JsonObject { ["x"] = 1 } };

        var answer = ToolAnswer.Ok(whole["part"]!);

        Assert.Equal("""{"ok":true,"data":{"x":1}}""", answer.Text());
        Assert.Equal("""{"part":{"x":1}}""", whole.ToJsonString());
    }

    /// <summary>The trap BuildBridge met: <c>Value is { }</c> holds for a failed <c>Answer&lt;bool&gt;</c>, so success is the error being null.</summary>
    [Fact]
    public void From_ASuccessOfAValueType_ThatIsItsDefault_IsASuccess()
    {
        var answer = ToolAnswer.From(Answer.Ok(false), stopped => new JsonObject { ["stopped"] = stopped });

        Assert.Equal("""{"ok":true,"data":{"stopped":false}}""", answer.Text());
        Assert.NotEqual(true, answer.IsError);
    }

    [Fact]
    public void From_AFailure_IsTheFailureEnvelope_WithItsType()
    {
        var answer = ToolAnswer.From(Answer.Failed<bool>("Not running.", "Start it first.", "stopped"), stopped => new JsonObject { ["stopped"] = stopped });

        Assert.Equal("""{"ok":false,"error":"Not running.","hint":"Start it first.","type":"stopped"}""", answer.Text());
        Assert.True(answer.IsError);
    }

    [Fact]
    public void Answer_IsASuccess_ExactlyWhenItHasNoError()
    {
        Assert.True(Answer.Ok(0).IsSuccess);
        Assert.False(Answer.Failed<int>("No.").IsSuccess);
        Assert.Equal(0, Answer.Failed<int>("No.").Value);
    }

    [Theory]
    [InlineData("""{"ok":false,"error":"x"}""", true)]
    [InlineData("""{"data":{"done":1},"ok":false}""", true)]
    [InlineData("""{"ok":true,"data":{"ok":false}}""", false)]
    [InlineData("""{"data":{"ok":false},"ok":true}""", false)]
    [InlineData("""{"data":{"ok":false}}""", false)]
    [InlineData("""[{"ok":false}]""", false)]
    [InlineData("""{"ok":false""", false)]
    [InlineData("""{"ok":"false"}""", false)]
    [InlineData("not json", false)]
    [InlineData("", false)]
    public void IsFailure_IsTheTopLevelOkBeingFalse(string text, bool isFailure) =>
        Assert.Equal(isFailure, ToolAnswer.IsFailure(new CallToolResult { Content = [new TextContentBlock { Text = text }] }));

    /// <summary>A picture after the envelope (ToolAnswer.WithImage) leaves the failure what it is.</summary>
    [Fact]
    public void IsFailure_OfTheEnvelopeFollowedByAPicture_IsTrue()
    {
        var answer = new CallToolResult
        {
            Content = [new TextContentBlock { Text = """{"ok":false,"error":"x"}""" }, new ImageContentBlock { Data = Encoding.UTF8.GetBytes("AAAA"), MimeType = "image/png" }],
        };

        Assert.True(ToolAnswer.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_OfAPictureFirst_IsFalse()
    {
        var answer = new CallToolResult
        {
            Content = [new ImageContentBlock { Data = Encoding.UTF8.GetBytes("AAAA"), MimeType = "image/png" }, new TextContentBlock { Text = """{"ok":false,"error":"x"}""" }],
        };

        Assert.False(ToolAnswer.IsFailure(answer));
    }

    [Fact]
    public void IsFailure_OfWhatFailedWrites_IsTrue_AndOfWhatOkWrites_IsFalse()
    {
        Assert.True(ToolAnswer.IsFailure(ToolAnswer.Failed("x")));
        Assert.False(ToolAnswer.IsFailure(ToolAnswer.Ok(new JsonObject { ["ok"] = false })));
    }

    [Theory]
    [InlineData(Stage.Queued, WordCase.Lower, "queued")]
    [InlineData(Stage.InProgress, WordCase.Lower, "inprogress")]
    [InlineData(Stage.BuildFailed, WordCase.Lower, "buildfailed")]
    [InlineData(Stage.Queued, WordCase.Camel, "queued")]
    [InlineData(Stage.InProgress, WordCase.Camel, "inProgress")]
    [InlineData(Stage.BuildFailed, WordCase.Camel, "buildFailed")]
    public void Word_SpellsAValueInTheCasingChosen(Stage stage, WordCase casing, string word) =>
        Assert.Equal(word, ToolAnswer.Word(stage, casing));

    [Fact]
    public void Word_ByDefault_IsLowerCase() =>
        Assert.Equal("inprogress", ToolAnswer.Word(Stage.InProgress));

    [Fact]
    public void Moment_IsRoundTripIso_WhateverTheCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

        try
        {
            Assert.Equal("2026-11-08T10:05:03.0000000+03:00", ToolAnswer.Moment(new DateTimeOffset(2026, 11, 8, 10, 5, 3, TimeSpan.FromHours(3))));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Wire_WritesAsAnswersAreWritten_AndIsFixed()
    {
        Assert.Equal("""{"a":"Привет <b>"}""", new JsonObject { ["a"] = "Привет <b>" }.ToJsonString(ToolAnswer.Wire));
        Assert.True(ToolAnswer.Wire.IsReadOnly);
    }

    [Fact]
    public void ObjectInfo_SerializesAJsonObject_WithoutAContextOfTheServers() =>
        Assert.Equal("""{"a":"é"}""", JsonSerializer.Serialize(new JsonObject { ["a"] = "é" }, ToolAnswer.ObjectInfo));

    [Fact]
    public void Envelope_OfAJob_IsTheReportAsData_WithWhatGoesBeside()
    {
        var report = new JobReport("job://sample/echo/1", JobState.Running, DateTimeOffset.UnixEpoch, null, 0.5, "half", null);

        var answer = JobAnswer.Envelope(report, new JsonObject { ["note"] = "Read job://sample/echo/1 when it ends." });

        Assert.Equal("""{"ok":true,"data":{"uri":"job://sample/echo/1","state":"running","startedAt":"1970-01-01T00:00:00+00:00","progress":0.5,"message":"half","note":"Read job://sample/echo/1 when it ends."}}""", answer.Text());
        Assert.NotEqual(true, answer.IsError);
    }

    [Fact]
    public void Envelope_OfAJob_CarriesTheReportAlone_AsStructuredContent()
    {
        var report = new JobReport("job://sample/echo/1", JobState.Succeeded, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, null, null);

        var answer = JobAnswer.Envelope(report, new JsonObject { ["note"] = "done" });

        Assert.Equal(report.ToJson(), answer.StructuredContent!.Value.GetRawText());
        Assert.Equal(report, answer.Report());
    }

    /// <summary>A job that failed within the wait used to answer ok, which a model reads as the work done.</summary>
    [Theory]
    [InlineData(JobState.Failed, "disk full", "disk full", "failed")]
    [InlineData(JobState.Failed, null, "The job failed.", "failed")]
    [InlineData(JobState.Cancelled, "step 3", "The job was cancelled before it ended.", "cancelled")]
    public void Envelope_OfAJobThatDidNotSucceed_IsAFailure_ThatStillCarriesTheReport(JobState state, string? message, string error, string type)
    {
        var report = new JobReport("job://sample/echo/1", state, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, message, null);

        var answer = JobAnswer.Envelope(report);

        Assert.True(answer.IsError);
        Assert.False(answer.IsOk());
        Assert.Equal(error, answer.Error());
        Assert.Contains($"\"type\":\"{type}\"", answer.Text(), StringComparison.Ordinal);
        Assert.Equal(report, answer.Report());
    }

    [Fact]
    public void TryGet_OfASuccess_HandsOverTheValue_AndOfAFailure_TheError()
    {
        var had = Answer.Ok("scene").TryGet(out var value);
        var failed = Answer.Failed<string>("No scene.", "Open one.", "noScene");

        Assert.True(had);
        Assert.Equal("scene", value);
        Assert.False(failed.TryGet(out _));
        Assert.Equal(9, failed.Error.Length);
    }

    /// <summary>A success of a nullable type may hold null: the value is handed over as the type says, never vouched for.</summary>
    [Fact]
    public void TryGet_OfASuccessHoldingNull_IsASuccess()
    {
        Assert.True(Answer.Ok<string?>(null).TryGet(out var value));
        Assert.Null(value);
    }

    [Fact]
    public void As_OfAFailure_KeepsItsErrorHintAndType()
    {
        var failed = Answer.Failed<int>("Host gone.", "Check ssh.", "unreachable");

        Assert.Equal(new Answer<string>(null, "Host gone.", "Check ssh.", "unreachable"), failed.As<string>());
    }

    [Fact]
    public void As_OfASuccess_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Answer.Ok(1).As<string>());

    [Fact]
    public void Map_MakesTheValueAnother_AndCarriesAFailureAsItIs()
    {
        var mapped = Answer.Ok(2).Map(count => new JsonObject { ["count"] = count });
        var failed = Answer.Failed<int>("No.", "Ask again.", "refused").Map(count => new JsonObject { ["count"] = count });

        Assert.Equal("""{"count":2}""", mapped.Value!.ToJsonString());
        Assert.Equal(("No.", "Ask again.", "refused"), (failed.Error, failed.Hint, failed.Type));
    }

    [Fact]
    public void Failed_OfAFailedAnswer_KeepsItsType_AndTakesData()
    {
        var answer = ToolAnswer.Failed(Answer.Failed<int>("Build broke.", "Read the log.", "build"), new JsonObject { ["log"] = "a.log" });

        Assert.Equal("""{"ok":false,"error":"Build broke.","hint":"Read the log.","type":"build","data":{"log":"a.log"}}""", answer.Text());
        Assert.True(answer.IsError);
    }

    [Fact]
    public void Failed_OfASuccessfulAnswer_Throws() =>
        Assert.Throws<ArgumentException>(() => ToolAnswer.Failed(Answer.Ok(1)));

    [Fact]
    public void From_AnAnswerOfJson_NeedsNoMapper()
    {
        var answer = ToolAnswer.From(Answer.Ok(new JsonObject { ["a"] = 1 }));
        var failed = ToolAnswer.From(Answer.Failed<JsonObject>("No.", type: "refused"));

        Assert.Equal("""{"ok":true,"data":{"a":1}}""", answer.Text());
        Assert.Equal("""{"a":1}""", answer.StructuredContent!.Value.GetRawText());
        Assert.Equal("""{"ok":false,"error":"No.","type":"refused"}""", failed.Text());
    }

    [Fact]
    public void Ok_OfTypedData_IsWrittenAsANodeWouldBe_WithItsDataAsStructuredContent()
    {
        var typed = ToolAnswer.Ok(new Outcome("Привет & <b>"), SampleJson.Default.Outcome);
        var node = ToolAnswer.Ok(new JsonObject { ["text"] = "Привет & <b>" });

        Assert.Equal(node.Text(), typed.Text());
        Assert.Equal("""{"text":"Привет & <b>"}""", typed.StructuredContent!.Value.GetRawText());
    }

    [Theory]
    [InlineData(AnswerShape.Envelope, """{"ok":true,"data":{"text":"x"}}""")]
    [InlineData(AnswerShape.None, null)]
    public void Ok_OfTypedData_CarriesTheShapeAsked(AnswerShape shape, string? structured) =>
        Assert.Equal(structured, ToolAnswer.Ok(new Outcome("x"), SampleJson.Default.Outcome, shape).StructuredContent?.GetRawText());

    [Fact]
    public void Ok_OfTypedDataThatIsNoObject_CarriesTextAlone()
    {
        var answer = ToolAnswer.Ok(42, ToolJsonInts.Default.Int32);

        Assert.Equal("""{"ok":true,"data":42}""", answer.Text());
        Assert.Null(answer.StructuredContent);
    }

    [Fact]
    public void Text_IsTheEnvelope_EvenWithBlocksAfterIt()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["files"] = 1 });
        answer.Content.Add(new TextContentBlock { Text = "a file named in a block of its own" });

        Assert.Equal("""{"ok":true,"data":{"files":1}}""", ToolAnswer.Text(answer));
    }

    [Fact]
    public void Text_OfAResultThatDoesNotOpenWithText_Throws() =>
        Assert.Throws<ArgumentException>(() => ToolAnswer.Text(new CallToolResult { Content = [] }));

    [Theory]
    [InlineData("""{"ok":true,"data":{"a":1}}""")]
    [InlineData("""{"ok":false,"error":"No.","hint":"Ask.","type":"refused","data":{"done":2}}""")]
    [InlineData("""{"ok":false,"error":"No."}""")]
    public void Of_AStoredEnvelope_AnswersAsTheToolFirstDid(string envelope)
    {
        var first = JsonNode.Parse(envelope)!.AsObject();

        var answer = ToolAnswer.Of(first);

        Assert.Equal(envelope, answer.Text());
        Assert.Equal(first["ok"]!.GetValue<bool>() ? null : true, answer.IsError);
        Assert.Equal(first["ok"]!.GetValue<bool>(), answer.StructuredContent is not null);
    }

    [Theory]
    [InlineData("""{"data":{}}""")]
    [InlineData("""{"ok":true}""")]
    [InlineData("""{"ok":false,"hint":"h"}""")]
    public void Of_AnObjectThatIsNoEnvelope_Throws(string text) =>
        Assert.Throws<ArgumentException>(() => ToolAnswer.Of(JsonNode.Parse(text)!.AsObject()));

    [Fact]
    public void Envelope_OfAJobThatFailedWithAnAnswer_CarriesItsHintAndType()
    {
        var report = new JobReport("job://sample/echo/1", JobState.Failed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "Host gone.", null) { Hint = "Check ssh.", Type = "unreachable" };

        var answer = JobAnswer.Envelope(report, hint: "Start it again.");

        Assert.Equal("Check ssh.", answer.Hint());
        Assert.Contains("\"type\":\"unreachable\"", answer.Text(), StringComparison.Ordinal);
        Assert.Equal(report, answer.Report());
    }

    [Theory]
    [InlineData(JobState.Failed, "failed")]
    [InlineData(JobState.Cancelled, "cancelled")]
    public void Envelope_OfAJobThatEndedWithoutAHint_TakesTheToolsOwn(JobState state, string type)
    {
        var report = new JobReport("job://sample/echo/1", state, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "boom", null);

        var answer = JobAnswer.Envelope(report, hint: "Start it again.");

        Assert.Equal("Start it again.", answer.Hint());
        Assert.Contains($"\"type\":\"{type}\"", answer.Text(), StringComparison.Ordinal);
    }

    /// <summary>#132: one hint for a failure and a cancellation alike had a server work its hint out before every call.</summary>
    [Theory]
    [InlineData(JobState.Failed, "Read its log.")]
    [InlineData(JobState.Cancelled, "Start it again.")]
    public void Envelope_WithOptions_TakesTheHintChosenForTheReport(JobState state, string hint)
    {
        var report = new JobReport("job://sample/echo/1", state, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "boom", null);
        var options = new JobAnswerOptions { Hint = ended => ended.State == JobState.Cancelled ? "Start it again." : "Read its log." };

        var answer = JobAnswer.Envelope(report, options: options);

        Assert.Equal(hint, answer.Hint());
        Assert.Equal(report, answer.Report());
    }

    /// <summary>#129: the error of a cancelled job was the SDK's alone; a server words it.</summary>
    [Fact]
    public void Result_WithOptions_WordsACancellationAsTheServerDoes()
    {
        var report = new JobReport("job://sample/scan/1", JobState.Cancelled, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "cancelled", null);
        var options = new JobAnswerOptions { Cancelled = "The scan was stopped before it ended." };

        var answer = JobAnswer.Result(report, options: options);

        Assert.True(answer.IsError);
        Assert.Equal("The scan was stopped before it ended.", answer.Error());
        Assert.Equal("cancelled", answer.Type());
        Assert.Null(answer.Hint());
    }

    /// <summary>The hint the work's answer gave is the job's own and wins over the one the server chooses; a running job is not asked about.</summary>
    [Fact]
    public void Result_WithOptions_KeepsTheReportsOwnHint_AndAsksNothingOfARunningJob()
    {
        var asked = 0;
        var options = new JobAnswerOptions { Hint = _ => $"asked {++asked}" };
        var failed = new JobReport("job://sample/scan/1", JobState.Failed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "Host gone.", null) { Hint = "Check ssh." };
        var running = new JobReport("job://sample/scan/2", JobState.Running, DateTimeOffset.UnixEpoch, null, null, null, null);

        var answered = JobAnswer.Result(failed, options: options);
        var runs = JobAnswer.Result(running, new JsonObject { ["note"] = "scanning" }, options: options);

        Assert.Equal("Check ssh.", answered.Hint());
        Assert.True(runs.IsOk());
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Result_OfAJobThatSucceeded_IsItsResultAsTheData_WithTheReportBeside()
    {
        var report = new JobReport("job://sample/up/1", JobState.Succeeded, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, null, JsonSerializer.SerializeToElement(new JsonObject { ["environment"] = "e2e-1" }, ToolAnswer.ObjectInfo));

        var answer = JobAnswer.Result(report, new JsonObject { ["note"] = "running" });

        Assert.Equal("""{"ok":true,"data":{"environment":"e2e-1"}}""", answer.Text());
        Assert.NotEqual(true, answer.IsError);
        Assert.Equal(report.ToJson(), answer.Report()!.ToJson());
    }

    [Fact]
    public void Result_OfAJobThatRuns_IsTheReport_WithWhatGoesBesideARunningJob()
    {
        var report = new JobReport("job://sample/up/1", JobState.Running, DateTimeOffset.UnixEpoch, null, null, null, null);

        var answer = JobAnswer.Result(report, new JsonObject { ["file"] = "out.csv" });

        Assert.Equal("""{"ok":true,"data":{"uri":"job://sample/up/1","state":"running","startedAt":"1970-01-01T00:00:00+00:00","file":"out.csv"}}""", answer.Text());
    }

    [Fact]
    public void Pending_OfARunningJob_IsTheReport_WithWhatGoesBeside_AndTheHint_AsASuccess()
    {
        var report = new JobReport("job://sample/up/1", JobState.Running, DateTimeOffset.UnixEpoch, null, 0.5, "half", null);

        var answer = JobAnswer.Pending(report, new JsonObject { ["note"] = "raising" }, "Read job://sample/up/1 when it ends.");

        Assert.Equal("""{"ok":true,"hint":"Read job://sample/up/1 when it ends.","data":{"uri":"job://sample/up/1","state":"running","startedAt":"1970-01-01T00:00:00+00:00","progress":0.5,"message":"half","note":"raising"}}""", answer.Text());
        Assert.NotEqual(true, answer.IsError);
        Assert.Equal(report, answer.Report());
    }

    /// <summary>#22: Envelope and Result build the running answer through Pending, to the byte.</summary>
    [Fact]
    public void Envelope_AndResult_OfARunningJob_ArePending()
    {
        var report = new JobReport("job://sample/up/1", JobState.Running, DateTimeOffset.UnixEpoch, null, null, null, null);
        var beside = new JsonObject { ["note"] = "raising" };
        var options = new JobAnswerOptions { Hint = _ => "never asked" };

        var pending = JobAnswer.Pending(report, beside);
        var envelope = JobAnswer.Envelope(report, beside, "Start it again.", options);
        var result = JobAnswer.Result(report, beside, "Raise again.", options);

        Assert.Equal(pending.Text(), envelope.Text());
        Assert.Equal(pending.Text(), result.Text());
        Assert.Equal(pending.StructuredContent!.Value.GetRawText(), envelope.StructuredContent!.Value.GetRawText());
        Assert.Equal(pending.StructuredContent!.Value.GetRawText(), result.StructuredContent!.Value.GetRawText());
        Assert.Equal(pending.IsError, envelope.IsError);
    }

    /// <summary>A report that ended before the answer was written is no mistake of the caller's: it is answered as Envelope answers it.</summary>
    [Theory]
    [InlineData(JobState.Succeeded)]
    [InlineData(JobState.Failed)]
    [InlineData(JobState.Cancelled)]
    public void Pending_OfAJobThatEnded_IsTheEnvelope(JobState state)
    {
        var report = new JobReport("job://sample/up/1", state, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "No manifest.", null);
        var beside = new JsonObject { ["note"] = "raising" };
        var options = new JobAnswerOptions { Cancelled = "Stopped." };

        var pending = JobAnswer.Pending(report, beside, "Start it again.", options);
        var envelope = JobAnswer.Envelope(report, beside, "Start it again.", options);

        Assert.Equal(envelope.Text(), pending.Text());
        Assert.Equal(envelope.IsError, pending.IsError);
        Assert.Equal(report, pending.Report());
    }

    /// <summary>A failure kept as the job's result used to be answered ok, its envelope inside the data (Snail.MCP.E2E, Connector).</summary>
    [Fact]
    public void Result_OfAJobThatFailedWithAnAnswer_IsThatFailure_FlaggedAndWithoutWhatGoesBesideARunningJob()
    {
        var report = new JobReport("job://sample/up/1", JobState.Failed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "No manifest.", null) { Hint = "Add one.", Type = "noManifest" };

        var answer = JobAnswer.Result(report, new JsonObject { ["note"] = "running" }, "Raise again.");

        Assert.True(answer.IsError);
        Assert.Equal("No manifest.", answer.Error());
        Assert.Equal("Add one.", answer.Hint());
        Assert.StartsWith("""{"ok":false,"error":"No manifest.","hint":"Add one.","type":"noManifest","data":{"uri":""", answer.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain("note", answer.Text(), StringComparison.Ordinal);
        Assert.Equal(report, answer.Report());
    }

    /// <summary>What a page told before it refused travelled as a failed answer's value, which From dropped (Snail.MCP.Playwright's Stopped).</summary>
    [Fact]
    public void Failure_WithDetails_IsAnsweredWithThemAsItsData()
    {
        Answer<string> stopped = Answer.Failed("The page refused.", "Come back later.", "refused", new JsonObject { ["retryAfter"] = 30 });

        var mapped = ToolAnswer.From(stopped, page => new JsonObject { ["page"] = page });
        var whole = ToolAnswer.Failed(stopped);

        Assert.Equal("""{"ok":false,"error":"The page refused.","hint":"Come back later.","type":"refused","data":{"retryAfter":30}}""", mapped.Text());
        Assert.Equal(mapped.Text(), whole.Text());
        Assert.True(mapped.IsError);
        Assert.Null(mapped.StructuredContent);
    }

    [Fact]
    public void Failure_WithDetails_OfAJsonValue_IsAnsweredWithThemAsItsData()
    {
        var stopped = Answer.Failed<JsonObject>("Step 2 failed.", details: new JsonObject { ["ran"] = new JsonArray("commit") });

        var answer = ToolAnswer.From(stopped);

        Assert.Equal("""{"ok":false,"error":"Step 2 failed.","data":{"ran":["commit"]}}""", answer.Text());
    }

    [Fact]
    public void Failed_WithDataOfItsOwn_AnswersThatData_RatherThanTheDetails()
    {
        var stopped = Answer.Failed<int>("No.", details: new JsonObject { ["learned"] = 1 });

        var answer = ToolAnswer.Failed(stopped, new JsonObject { ["told"] = 2 });

        Assert.Equal(2, answer.Data()!["told"]!.GetValue<int>());
        Assert.Null(answer.Data()!["learned"]);
    }

    [Fact]
    public void Details_GoOnWithTheFailure_IntoAnAnswerOfAnotherType()
    {
        var stopped = Answer.Failed<int>("No.", "Ask again.", "refused", new JsonObject { ["learned"] = 1 });

        var carried = stopped.As<string>();
        var mapped = stopped.Map(count => new JsonObject { ["count"] = count });

        Assert.Same(stopped.Details, carried.Details);
        Assert.Same(stopped.Details, mapped.Details);
        Assert.Equal(("No.", "Ask again.", "refused"), (mapped.Error, mapped.Hint, mapped.Type));
    }

    /// <summary>#149: Snail.MCP.Memory passed a JSON mapper down to its domain layer only to put the duplicate record into the failure.</summary>
    [Fact]
    public void Failure_WithTypedDetails_IsAnsweredWithThemWrittenByTheTool()
    {
        Answer<int> refused = Answer.Failed("Not stored: a near duplicate.", "pass its id as replaceId", "duplicate").WithDetails(new Duplicate("r-1", 0.97));

        var answer = ToolAnswer.From<int, Duplicate>(refused, count => JsonValue.Create(count), duplicate => new JsonObject { ["id"] = duplicate.Id });

        Assert.Equal("""{"ok":false,"error":"Not stored: a near duplicate.","hint":"pass its id as replaceId","type":"duplicate","data":{"id":"r-1"}}""", answer.Text());
        Assert.True(answer.IsError);
    }

    /// <summary>A test of the domain layer reads back what it handed on, through As and Map as well.</summary>
    [Fact]
    public void TypedDetails_GoOnWithTheFailure_AndAreReadBack()
    {
        var refused = Answer.Failed<int>("No.").WithDetails(new Duplicate("r-1", 0.97));

        var carried = refused.As<string>().Map(text => text.Length);

        Assert.Equal("r-1", carried.TryGetDetails<Duplicate>(out var duplicate) ? duplicate.Id : null);
        Assert.False(carried.TryGetDetails<string>(out _));
        Assert.Null(carried.Details);
    }

    /// <summary>Only a tool that maps the details writes them; the success is written as ever, and a failure of other details keeps its JSON.</summary>
    [Fact]
    public void From_WithADetailsMapper_WritesASuccess_AndTheJsonDetailsOfAFailureNotOfThatType()
    {
        static JsonNode Written(Duplicate duplicate) => new JsonObject { ["id"] = duplicate.Id };

        var success = ToolAnswer.From<int, Duplicate>(Answer.Ok(3), count => JsonValue.Create(count), Written);
        var json = ToolAnswer.From<int, Duplicate>(Answer.Failed<int>("No.", details: new JsonObject { ["learned"] = 1 }), count => JsonValue.Create(count), Written);
        var unmapped = ToolAnswer.From(Answer.Failed<int>("No.").WithDetails(new Duplicate("r-1", 0.97)), count => JsonValue.Create(count));

        Assert.Equal("""{"ok":true,"data":3}""", success.Text());
        Assert.Equal(1, json.Data()!["learned"]!.GetValue<int>());
        Assert.Equal("""{"ok":false,"error":"No."}""", unmapped.Text());
    }

    /// <summary>JSON is details already: handed on as a value, it is the failure's Details itself.</summary>
    [Fact]
    public void WithDetails_OfJson_IsTheJsonDetails()
    {
        var learned = new JsonObject { ["ran"] = 2 };

        var refused = Answer.Failed<int>("No.").WithDetails(learned);

        Assert.Same(learned, refused.Details);
        Assert.Equal(2, ToolAnswer.Failed(refused).Data()!["ran"]!.GetValue<int>());
        Assert.Throws<InvalidOperationException>(() => Answer.Ok(1).WithDetails(learned));
    }

    private sealed record Duplicate(string Id, double Similarity);

    /// <summary>The type argument told the compiler nothing it did not know from the return type (Snail.MCP.CleanUp, 28 places).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Failure_WithoutATypeArgument_IsAnAnswerOfTheTypeExpected(bool isFound)
    {
        Answer<JsonObject> answer = isFound ? Answer.Ok(new JsonObject { ["found"] = 1 }) : Answer.Failed("Not found.", "List first.", "missing");

        Assert.Equal(isFound, answer.IsSuccess);
        Assert.Equal(isFound ? null : "Not found.", answer.Error);
        Assert.Equal(isFound ? null : "missing", answer.Type);
    }

    [Fact]
    public void Failure_WithoutATypeArgument_EqualsTheOneWithIt()
    {
        static Answer<int> Count(string? text) => text is null ? Answer.Failed("No text.", type: "empty") : Answer.Ok(text.Length);

        Assert.Equal(Answer.Failed<int>("No text.", type: "empty"), Count(null));
        Assert.Equal(Answer.Ok(3), Count("abc"));
        Assert.Equal(Answer.Failed<int>("No.", details: null), Answer.Failed("No.").As<int>());
    }

    [Fact]
    public async Task Failure_WithoutATypeArgument_IsReturnedFromAnAsyncLambda()
    {
        Func<bool, Task<Answer<int>>> count = async isEmpty =>
        {
            await Task.Yield();

            if (isEmpty)
                return Answer.Failed("Nothing to count.");

            return Answer.Ok(3);
        };

        Assert.Equal("Nothing to count.", (await count(true)).Error);
        Assert.Equal(3, (await count(false)).Value);
    }

    /// <summary>Playwright, AfterEffects, Blender and Figma each added the picture by hand.</summary>
    [Fact]
    public void WithImage_AddsThePictureAfterTheEnvelope_WhichStaysTheText()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47];

        var answer = ToolAnswer.Ok(new JsonObject { ["title"] = "Home" }, AnswerShape.None).WithImage(png, "image/png");

        Assert.Equal(2, answer.Content.Count);
        Assert.Equal("""{"ok":true,"data":{"title":"Home"}}""", ToolAnswer.Text(answer));
        Assert.Equal("""{"ok":true,"data":{"title":"Home"}}""", answer.Text());
        var image = Assert.Single(answer.Images());
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(png, image.DecodedData.ToArray());
        Assert.True(answer.IsOk());
        Assert.False(ToolAnswer.IsFailure(answer));
    }

    [Fact]
    public void WithImage_OnAFailure_StaysAFailure()
    {
        var answer = ToolAnswer.Failed("The page refused.").WithImage(new byte[] { 1 }, "image/jpeg");

        Assert.True(answer.IsError);
        Assert.True(ToolAnswer.IsFailure(answer));
        Assert.Equal("The page refused.", answer.Error());
    }

    [Fact]
    public void WithImage_WithoutBytesOrAType_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => ToolAnswer.Ok(new JsonObject()).WithImage(ReadOnlyMemory<byte>.Empty, "image/png"));
        Assert.Throws<ArgumentException>(() => ToolAnswer.Ok(new JsonObject()).WithImage(new byte[] { 1 }, " "));
    }

    [Fact]
    public void IsFailure_OfAFailureFollowedByText_IsNot()
    {
        var answer = ToolAnswer.Failed("No.");
        answer.Content.Add(new TextContentBlock { Text = "more" });

        Assert.False(ToolAnswer.IsFailure(answer));
    }

    /// <summary>Snail.MCP.Figma's contract reads fileKey, source and metrics beside the data on 17 tools; the envelope had no place for them.</summary>
    [Fact]
    public void Ok_WithFieldsBeside_WritesThemAfterTheData_AndTheStructuredContentStaysTheData()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["id"] = "1:2" }, AnswerShape.Data, new JsonObject { ["fileKey"] = "abc", ["source"] = "cache", ["metrics"] = new JsonObject { ["bytesIn"] = 10 } });

        Assert.Equal("""{"ok":true,"data":{"id":"1:2"},"fileKey":"abc","source":"cache","metrics":{"bytesIn":10}}""", answer.Text());
        Assert.Equal("""{"id":"1:2"}""", answer.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public void Ok_WithFieldsBeside_ShapedAsTheEnvelope_CarriesThemInTheStructuredContent()
    {
        var answer = ToolAnswer.Ok(new JsonArray(1, 2), AnswerShape.Envelope, new JsonObject { ["nodeId"] = "1:2" });

        Assert.Equal("""{"ok":true,"data":[1,2],"nodeId":"1:2"}""", answer.StructuredContent!.Value.GetRawText());
        Assert.Equal(answer.Text(), answer.StructuredContent.Value.GetRawText());
    }

    [Fact]
    public void Ok_WithAFieldBesideThatIsNull_LeavesItOut()
    {
        var answer = ToolAnswer.Ok(new JsonObject(), AnswerShape.None, new JsonObject { ["staleReason"] = null, ["source"] = "figma-api" });

        Assert.Equal("""{"ok":true,"data":{},"source":"figma-api"}""", answer.Text());
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("error")]
    [InlineData("hint")]
    [InlineData("type")]
    [InlineData("data")]
    public void Ok_WithAFieldBesideNamedAsTheEnvelopesOwn_Throws(string name) =>
        Assert.Throws<ArgumentException>(() => ToolAnswer.Ok(new JsonObject(), AnswerShape.Data, new JsonObject { [name] = 1 }));

    [Theory]
    [InlineData("""{"ok":true,"data":{"a":1},"fileKey":"abc","metrics":{"bytesIn":3}}""")]
    [InlineData("""{"ok":false,"error":"No.","hint":"Ask.","type":"refused","data":{"done":2},"fileKey":"abc"}""")]
    public void Of_AStoredEnvelopeWithFieldsBeside_KeepsThem(string envelope)
    {
        var answer = ToolAnswer.Of(JsonNode.Parse(envelope)!.AsObject());

        Assert.Equal(envelope, answer.Text());
    }

    [Fact]
    public void Of_AStoredEnvelopeWithAFieldBeforeTheData_WritesItAfter()
    {
        var answer = ToolAnswer.Of(JsonNode.Parse("""{"fileKey":"abc","ok":true,"data":{"a":1}}""")!.AsObject());

        Assert.Equal("""{"ok":true,"data":{"a":1},"fileKey":"abc"}""", answer.Text());
        Assert.Equal("""{"a":1}""", answer.StructuredContent!.Value.GetRawText());
    }

    /// <summary>136 tools of Snail.MCP.Figma return a string: <c>ToolAnswer.Text(ToolAnswer.Ok(data, AnswerShape.None))</c> each time was long.</summary>
    [Fact]
    public void OkText_IsTheTextOfTheAnswer()
    {
        var data = new JsonObject { ["name"] = "Кнопка<T>" };

        Assert.Equal(ToolAnswer.Text(ToolAnswer.Ok(data, AnswerShape.None)), ToolAnswer.OkText(data));
        Assert.Equal("""{"ok":true,"data":{"name":"Кнопка<T>"}}""", ToolAnswer.OkText(data));
    }

    [Fact]
    public void OkText_WithFieldsBeside_IsTheTextOfTheAnswer()
    {
        var data = new JsonObject { ["id"] = "1:2" };
        var beside = new JsonObject { ["fileKey"] = "abc" };

        Assert.Equal(ToolAnswer.Text(ToolAnswer.Ok(data, AnswerShape.None, beside)), ToolAnswer.OkText(data, beside));
    }

    /// <summary>#142: three tools of Snail.MCP.Figma answer a success with what to do next, and wrote the envelope by hand since a hint beside was refused.</summary>
    [Fact]
    public void Ok_WithAHint_WritesItInItsPlaceBeforeTheData_AndTheEnvelopeShapeCarriesIt()
    {
        var data = new JsonObject { ["version"] = "42" };

        var plain = ToolAnswer.Ok(data, AnswerShape.Data, hint: "Name the version next time.");
        var shaped = ToolAnswer.Ok(data, AnswerShape.Envelope, new JsonObject { ["fileKey"] = "abc" }, "Name the version next time.");

        Assert.Equal("""{"ok":true,"hint":"Name the version next time.","data":{"version":"42"}}""", plain.Text());
        Assert.Equal("""{"version":"42"}""", plain.StructuredContent!.Value.GetRawText());
        Assert.Equal("""{"ok":true,"hint":"Name the version next time.","data":{"version":"42"},"fileKey":"abc"}""", shaped.StructuredContent!.Value.GetRawText());
        Assert.True(plain.IsOk());
        Assert.Equal("Name the version next time.", plain.Hint());
    }

    [Fact]
    public void OkText_WithAHint_IsTheTextOfTheAnswer()
    {
        var data = new JsonObject { ["found"] = 120 };

        Assert.Equal(ToolAnswer.Text(ToolAnswer.Ok(data, AnswerShape.None, hint: "Narrow the query.")), ToolAnswer.OkText(data, hint: "Narrow the query."));
        Assert.Equal("""{"ok":true,"hint":"Narrow the query.","data":{"found":120}}""", ToolAnswer.OkText(data, hint: "Narrow the query."));
    }

    /// <summary>A stored success kept its fields beside the data but dropped its hint.</summary>
    [Fact]
    public void Of_AStoredSuccessWithAHint_KeepsIt()
    {
        const string envelope = """{"ok":true,"hint":"Narrow the query.","data":{"found":120},"source":"rest"}""";

        var answer = ToolAnswer.Of(JsonNode.Parse(envelope)!.AsObject());

        Assert.Equal(envelope, answer.Text());
        Assert.Equal("Narrow the query.", answer.Hint());
    }

    [Fact]
    public void FailedText_IsTheTextOfTheFailure_ThatExplainFailuresFlags()
    {
        var text = ToolAnswer.FailedText("No such node.", "List the page first.", "missing", new JsonObject { ["done"] = 1 });

        Assert.Equal(ToolAnswer.Text(ToolAnswer.Failed("No such node.", "List the page first.", "missing", new JsonObject { ["done"] = 1 })), text);
        Assert.True(ToolAnswer.IsFailure(new CallToolResult { Content = [new TextContentBlock { Text = text }] }));
    }

    [Fact]
    public void From_ASuccessOfTypedData_IsWrittenAsOkWritesIt()
    {
        var read = new FileRead("a.txt", 3);

        var answer = ToolAnswer.From(Answer.Ok(read), EnvelopeJson.Default.FileRead);

        Assert.Equal(ToolAnswer.Ok(read, EnvelopeJson.Default.FileRead).Text(), answer.Text());
        Assert.Equal("""{"path":"a.txt","lines":3}""", answer.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public void From_ASuccessOfTypedData_CarriesTheShapeAsked()
    {
        var answer = ToolAnswer.From(Answer.Ok(new FileRead("a.txt", 3)), EnvelopeJson.Default.FileRead, AnswerShape.None);

        Assert.Null(answer.StructuredContent);
    }

    [Fact]
    public void From_AFailureOfTypedData_IsTheFailureEnvelope_WithItsDetails()
    {
        Answer<FileRead> failed = Answer.Failed("No such file.", "List the folder.", "missing", new JsonObject { ["tried"] = "a.txt" });

        var answer = ToolAnswer.From(failed, EnvelopeJson.Default.FileRead);

        Assert.True(answer.IsError);
        Assert.Equal("""{"ok":false,"error":"No such file.","hint":"List the folder.","type":"missing","data":{"tried":"a.txt"}}""", answer.Text());
    }

    /// <summary>Snail.MCP.FileSystem kept Answered&lt;T&gt; for its output schemas; the envelope type is the SDK's now, and the same JSON.</summary>
    [Fact]
    public void Envelope_OfASuccess_IsTheStructuredContentAndTheTextOfAnEnvelopeAnswer()
    {
        var read = new FileRead("a.txt", 3);

        var answer = ToolAnswer.Ok(JsonSerializer.SerializeToNode(read, EnvelopeJson.Default.FileRead)!, AnswerShape.Envelope);
        var typed = JsonSerializer.Serialize(new Envelope<FileRead>(true, read), EnvelopeJson.Default.EnvelopeFileRead);

        Assert.Equal(answer.Text(), typed);
        Assert.Equal(typed, answer.StructuredContent!.Value.GetRawText());
        Assert.Equal(new Envelope<FileRead>(true, read), JsonSerializer.Deserialize(answer.StructuredContent.Value, EnvelopeJson.Default.EnvelopeFileRead));
    }

    [Fact]
    public void Envelope_OfAFailure_IsTheTextOfAFailedAnswer()
    {
        var answer = ToolAnswer.Failed("No such file.", "List the folder.", "missing");

        var typed = JsonSerializer.Serialize(new Envelope<FileRead>(false, null!, "No such file.", "List the folder.", "missing"), EnvelopeJson.Default.EnvelopeFileRead);

        Assert.Equal(answer.Text(), typed);
    }

    [Fact]
    public void Envelope_AsTheDeclaredOutput_PublishesItsSchema_OkFirst()
    {
        var method = typeof(ToolAnswerTests).GetMethod(nameof(ReadFile), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var tool = ModelContextProtocol.Server.McpServerTool.Create(method, (object?)null, new ModelContextProtocol.Server.McpServerToolCreateOptions { SerializerOptions = ToolJson.Options(EnvelopeJson.Default) });

        var schema = tool.ProtocolTool.OutputSchema!.Value;
        var properties = schema.GetProperty("properties");
        Assert.Equal(["ok", "error", "hint", "type", "data"], properties.EnumerateObject().Select(property => property.Name));
        Assert.Equal("boolean", properties.GetProperty("ok").GetProperty("type").GetString());
        Assert.True(properties.GetProperty("data").ToString().Contains("\"lines\"", StringComparison.Ordinal));
    }

    /// <summary>The schema is a success's, and a success always has its data: null published as allowed let a client take an empty success for a valid one (Snail.MCP.Figma).</summary>
    [Fact]
    public void Envelope_AsTheDeclaredOutput_PublishesDataAsRequiredAndNeverNull()
    {
        var method = typeof(ToolAnswerTests).GetMethod(nameof(ReadFile), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var tool = ModelContextProtocol.Server.McpServerTool.Create(method, (object?)null, new ModelContextProtocol.Server.McpServerToolCreateOptions { SerializerOptions = ToolJson.Options(EnvelopeJson.Default) });

        var schema = tool.ProtocolTool.OutputSchema!.Value;
        Assert.Equal("object", schema.GetProperty("properties").GetProperty("data").GetProperty("type").GetString());
        Assert.Contains("data", schema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
    }

    [ModelContextProtocol.Server.McpServerTool(Name = "read_file", UseStructuredContent = true, OutputSchemaType = typeof(Envelope<FileRead>))]
    [System.ComponentModel.Description("Reads a file.")]
    private static CallToolResult ReadFile() => ToolAnswer.Ok(new JsonObject { ["path"] = "a.txt", ["lines"] = 3 }, AnswerShape.Envelope);

    /// <summary>What a sample file tool answers.</summary>
    public sealed record FileRead(string Path, int Lines);

    public enum Stage
    {
        Queued,
        InProgress,
        BuildFailed,
    }
}

[JsonSerializable(typeof(int))]
internal sealed partial class ToolJsonInts : JsonSerializerContext;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ToolAnswerTests.FileRead))]
[JsonSerializable(typeof(Envelope<ToolAnswerTests.FileRead>))]
internal sealed partial class EnvelopeJson : JsonSerializerContext;
