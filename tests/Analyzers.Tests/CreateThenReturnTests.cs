namespace Snail.Sdk.Analyzers.Tests;

/// <summary>The S3L0002 fix writes a declaration that compiles, in the line breaks the document already uses.</summary>
public sealed class CreateThenReturnTests
{
    private const string Point = """

        class Point
        {
            public int A { get; set; }
            public int B { get; set; }
            public int C { get; set; }
            public int D { get; set; }
            public int E { get; set; }
            public int F { get; set; }
            public int G { get; set; }
            public int H { get; set; }
            public int I { get; set; }
            public int J { get; set; }
            public int K { get; set; }
            public int L { get; set; }
            public int M { get; set; }
        }
        """;

    private static readonly string Numbers = string.Join("\n", Enumerable.Range(1, 16).Select(number => $"            {number},"));

    private static readonly string Assignments = string.Join("\n", "ABCDEFGHIJKLM".Select((letter, index) => $"            {letter} = {index + 1},"));

    /// <summary>A collection expression has no type of its own: the variable takes the one it converts to.</summary>
    [Fact]
    public Task CollectionExpression_IsDeclaredWithItsConvertedType() => Verify<ReturnShape>.FixAsync<CreateThenReturn>($$"""
        using System.Collections.Generic;

        static class Sample
        {
            static IReadOnlyList<int> Numbers()
            {
                return {|S3L0002:[
        {{Numbers}}
                ]|};
            }
        }
        """, $$"""
        using System.Collections.Generic;

        static class Sample
        {
            static IReadOnlyList<int> Numbers()
            {
                IReadOnlyList<int> result = [
        {{Numbers}}
                ];

                return result;
            }
        }
        """);

    /// <summary>A target-typed new names no type either: the variable takes the one it converts to.</summary>
    [Fact]
    public Task TargetTypedNew_IsDeclaredWithItsConvertedType() => Verify<ReturnShape>.FixAsync<CreateThenReturn>($$"""
        static class Sample
        {
            static Point Make()
            {
                return {|S3L0002:new()
                {
        {{Assignments}}
                }|};
            }
        }
        {{Point}}
        """, $$"""
        static class Sample
        {
            static Point Make()
            {
                Point point = new()
                {
        {{Assignments}}
                };

                return point;
            }
        }
        {{Point}}
        """);

    /// <summary>A type that cannot be written leaves nothing to declare the variable with, so no fix is offered.</summary>
    [Fact]
    public Task TargetTypedNew_OfAnUnknownType_IsNotFixed()
    {
        var source = $$"""
            static class Sample
            {
                static {|CS0246:Missing|} Make()
                {
                    return {|S3L0002:new()
                    {
            {{Assignments}}
                    }|};
                }
            }
            """;

        return Verify<ReturnShape>.FixAsync<CreateThenReturn>(source, source);
    }

    /// <summary>A CRLF document stays CRLF: the declaration and the blank line before the return end the way its lines do.</summary>
    [Fact]
    public Task Fix_InACrlfDocument_WritesCrlf() => Verify<ReturnShape>.FixAsync<CreateThenReturn>($$"""
        static class Sample
        {
            static Point Make()
            {
                return {|S3L0002:new Point
                {
        {{Assignments}}
                }|};
            }
        }
        {{Point}}
        """.ReplaceLineEndings("\r\n"), $$"""
        static class Sample
        {
            static Point Make()
            {
                var point = new Point
                {
        {{Assignments}}
                };

                return point;
            }
        }
        {{Point}}
        """.ReplaceLineEndings("\r\n"));
}
