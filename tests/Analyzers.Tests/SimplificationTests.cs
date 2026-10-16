namespace Snail.Sdk.Analyzers.Tests;

public sealed class SimplificationTests
{
    [Fact]
    public Task This_BeforeAnUnshadowedField_IsRemoved() => Verify<Simplification>.FixAsync<RemoveThis>("""
        sealed class Greeting(string name)
        {
            private readonly string _name = name;

            public string Text() => {|S3L0009:this|}._name;
        }
        """, """
        sealed class Greeting(string name)
        {
            private readonly string _name = name;

            public string Text() => _name;
        }
        """);

    [Fact]
    public Task This_BeforeAFieldAParameterShadows_IsLeftAlone() => Verify<Simplification>.AnalyzerAsync("""
        sealed class Greeting
        {
            private readonly string name;

            public Greeting(string name) => this.name = name;

            public string Text() => name;
        }
        """);

    [Fact]
    public Task FrameworkTypeName_IsWrittenAsItsKeyword() => Verify<Simplification>.FixAsync<UseKeyword>("""
        using System;

        static class Sample
        {
            static {|S3L0010:Int32|} Length({|S3L0010:System.String|} text) => {|S3L0010:String|}.Concat(text, "!").Length;
        }
        """, """
        using System;

        static class Sample
        {
            static int Length(string text) => string.Concat(text, "!").Length;
        }
        """);

    /// <summary>An alias is looked at too: the name it gives a framework type stands where the keyword would.</summary>
    [Fact]
    public Task Alias_OfAFrameworkType_IsWrittenAsItsKeyword() => Verify<Simplification>.AnalyzerAsync("""
        using Count = System.Int32;

        static class Sample
        {
            static {|S3L0010:Count|} Length(string text) => text.Length;
        }
        """);

    /// <summary>var binds to the type it infers; it is not a framework name standing where a keyword would.</summary>
    [Fact]
    public Task Var_OfABuiltInType_IsNotAKeywordType() => Verify<Simplification>.AnalyzerAsync("""
        static class Text
        {
            static int Length()
            {
                var text = "hello";
                var count = text.Length;

                return count;
            }
        }
        """);
}
