using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>What a test of a server reads from an answer, without parsing its text itself.</summary>
public sealed class ToolAnswersTests
{
    [Fact]
    public void Success_ReadsAsOk_WithItsData_AndNoError()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["count"] = 2 });

        Assert.True(answer.IsOk());
        Assert.Equal(2, answer.Data()!["count"]!.GetValue<int>());
        Assert.Null(answer.Error());
        Assert.Null(answer.Hint());
    }

    [Fact]
    public void Failure_ReadsAsNotOk_WithItsErrorHintAndData()
    {
        var answer = ToolAnswer.Failed("No such file.", "List the folder first.", data: new JsonObject { ["done"] = 1 });

        Assert.False(answer.IsOk());
        Assert.Equal("No such file.", answer.Error());
        Assert.Equal("List the folder first.", answer.Hint());
        Assert.Equal(1, answer.Data()!["done"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("plain words")]
    [InlineData("""{"data":1}""")]
    [InlineData("""[1,2]""")]
    public void Envelope_ThatIsNotOne_FailsTheTest_NamingTheText(string text)
    {
        var answer = new CallToolResult { Content = [new TextContentBlock { Text = text }] };

        var failure = Assert.Throws<InvalidOperationException>(() => answer.IsOk());

        Assert.Contains(text, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_OfAJobsAnswer_IsTheJob()
    {
        var report = new JobReport("job://sample/echo/1", JobState.Failed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, "broke", null);

        var answer = JobAnswer.Of(report, _ => ToolAnswer.Failed("broke"));

        Assert.Equal(report, answer.Report());
    }

    [Fact]
    public void Type_IsTheKindOfFailure_OrNull()
    {
        Assert.Equal("missing", ToolAnswer.Failed("No.", type: "missing").Type());
        Assert.Null(ToolAnswer.Failed("No.").Type());
        Assert.Null(ToolAnswer.Ok(new JsonObject()).Type());
    }

    [Fact]
    public void Images_OfAnAnswerWithoutPictures_AreNone() =>
        Assert.Empty(ToolAnswer.Ok(new JsonObject()).Images());

    [Fact]
    public void Text_OfAnAnswerThatDoesNotOpenWithText_FailsTheTest()
    {
        var answer = new CallToolResult { Content = [ImageContentBlock.FromBytes(new byte[] { 1 }, "image/png")] };

        var failure = Assert.Throws<InvalidOperationException>(() => answer.Text());

        Assert.Contains("image", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_OfAnAnswerThatIsNoJob_IsNull()
    {
        Assert.Null(ToolAnswer.Ok(new JsonObject { ["count"] = 2 }).Report());
        Assert.Null(ToolAnswer.Failed("No.").Report());
    }
}
