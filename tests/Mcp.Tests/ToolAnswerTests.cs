using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Fact]
    public void IsFailure_OfMoreThanOneBlock_IsFalse()
    {
        var answer = new CallToolResult
        {
            Content = [new TextContentBlock { Text = """{"ok":false,"error":"x"}""" }, new ImageContentBlock { Data = Encoding.UTF8.GetBytes("AAAA"), MimeType = "image/png" }],
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

    public enum Stage
    {
        Queued,
        InProgress,
        BuildFailed,
    }
}
