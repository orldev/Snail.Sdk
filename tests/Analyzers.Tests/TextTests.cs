namespace Snail.Sdk.Analyzers.Tests;

public sealed class TextTests
{
    [Fact]
    public Task Comment_InsideAMethod_IsReported() => Verify<SourceText>.AnalyzerAsync("""
        static class Sample
        {
            /// <summary>Documentation is not a comment inside a body.</summary>
            static int Run()
            {
                {|S3L0004:// explains what the name should|}
                return 1;
            }
        }
        """);

    [Fact]
    public Task Comment_AboveAMember_IsLeftAlone() => Verify<SourceText>.AnalyzerAsync("""
        static class Sample
        {
            // A remark above the member, not inside it.
            static int Run() => 1;
        }
        """);

    [Fact]
    public Task CyrillicName_IsReported() => Verify<SourceText>.AnalyzerAsync("""
        static class Sample
        {
            static int {|S3L0005:Счёт|}() => 1;
        }
        """);

    [Fact]
    public Task CyrillicText_InShippedCode_IsReported() => Verify<SourceText>.AnalyzerAsync("""
        static class Sample
        {
            static string Greeting() => {|S3L0005:"Привет"|};
        }
        """);

    [Fact]
    public Task CyrillicText_InATestProject_IsData() => Verify<SourceText>.AnalyzerAsync("""
        static class Sample
        {
            static string Input() => "Привет";
        }
        """, projectKind: "Tests");

    [Fact]
    public Task CyrillicText_InAPromptsType_IsExempt() => Verify<SourceText>.AnalyzerAsync("""
        static class GuidancePrompts
        {
            public const string Reply = "Отвечай по-русски";
        }
        """);
}
