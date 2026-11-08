namespace Snail.Sdk.Mcp.Programs.Tests;

/// <summary>Programs run by a native binary: what the JIT forgives and Native AOT does not, checked where it fails.</summary>
/// <remarks>
/// Every server of the fleet ships native. Jint hands a call whose arguments are all literals (or none) its own cache,
/// an <c>object[]</c> reinterpreted as <c>JsValue[]</c>; <c>log</c> read it as <c>IEnumerable&lt;JsValue&gt;</c>, and under
/// Native AOT the interface dispatch on that array failed fast — the whole server gone, SIGABRT and nothing on stderr —
/// while <c>log.call(null, 1)</c>, which passes a real array, and every test run by the JIT passed (Snail.MCP.Git, native).
/// </remarks>
public sealed class NativeProgramTests(NativeSample sample) : IClassFixture<NativeSample>
{
    [NativeTheory]
    [InlineData("log(1)", "1")]
    [InlineData("log()", "")]
    [InlineData("const l = log; l()", "")]
    [InlineData("globalThis.log(1)", "1")]
    [InlineData("log.call(null, 1)", "1")]
    [InlineData("log('a', 2)", "a 2")]
    [InlineData("const n = 3; log('n', n)", "n 3")]
    [InlineData("log('a', { b: 1 })", "a {\"b\":1}")]
    public void Program_CallingLog_InANativeServer_StaysUpAndWritesTheLine(string code, string line)
    {
        var answer = sample.Run(code);

        Assert.Null(answer["failure"]);
        Assert.Equal([line], answer["log"]!.AsArray().Select(written => written!.GetValue<string>()));
    }

    [NativeTheory]
    [InlineData("return tools.echo()", "{}")]
    [InlineData("return tools.echo({ n: 1 })", "{\"n\":1}")]
    [InlineData("const f = tools.echo; return f({ n: 2 })", "{\"n\":2}")]
    public void Program_CallingACommand_InANativeServer_GetsItsAnswer(string code, string result)
    {
        var answer = sample.Run(code);

        Assert.Null(answer["failure"]);
        Assert.Equal(result, answer["result"]!.ToJsonString());
    }

    [NativeTheory]
    [InlineData("tools.nothing()")]
    [InlineData("tools.nothing(1)")]
    public void Program_CallingAMissingCommand_InANativeServer_StopsWithTheNearestNames(string code)
    {
        var answer = sample.Run(code);

        Assert.Equal("no command 'nothing'", answer["failure"]!.GetValue<string>());
    }
}
