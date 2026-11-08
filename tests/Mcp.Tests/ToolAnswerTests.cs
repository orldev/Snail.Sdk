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

        var typed = JsonSerializer.Serialize(new Envelope<FileRead>(false, null, "No such file.", "List the folder.", "missing"), EnvelopeJson.Default.EnvelopeFileRead);

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
