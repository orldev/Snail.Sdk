namespace Snail.Sdk.Analyzers.Tests;

public sealed class ShapeTests
{
    [Fact]
    public Task Chain_OfSixOperators_IsUnpacked() => Verify<QueryShape>.AnalyzerAsync("""
        using System.Linq;

        static class Sample
        {
            static object Run(int[] items) =>
                {|S3L0001:items.Where(x => x > 0).Select(x => x * 2).OrderBy(x => x).Skip(1).Take(2).ToList()|};
        }
        """);

    [Fact]
    public Task Chain_OfFourOperators_IsLeftAlone() => Verify<QueryShape>.AnalyzerAsync("""
        using System.Linq;

        static class Sample
        {
            static object Run(int[] items) => items.Where(x => x > 0).Select(x => x * 2).OrderBy(x => x).ToList();
        }
        """);

    [Fact]
    public Task Chain_OverABuilder_IsNoQuery() => Verify<QueryShape>.AnalyzerAsync("""
        using System.Text;

        static class Sample
        {
            static string Run(int count) =>
                new StringBuilder().Append('[').Append(count).Append(", ").Append(count + 1).Append(']').Append('!').ToString();
        }
        """);

    [Fact]
    public Task Chain_OverAnObservable_IsUnpacked() => Verify<QueryShape>.AnalyzerAsync("""
        using System;

        static class Operators
        {
            public static IObservable<int> Where(this IObservable<int> source, Func<int, bool> keep) => source;
            public static IObservable<int> Select(this IObservable<int> source, Func<int, int> map) => source;
            public static IObservable<int> Skip(this IObservable<int> source, int count) => source;
            public static IObservable<int> Take(this IObservable<int> source, int count) => source;
            public static IObservable<int> Distinct(this IObservable<int> source) => source;
        }

        static class Sample
        {
            static IObservable<int> Run(IObservable<int> ticks) =>
                {|S3L0001:ticks.Where(x => x > 0).Select(x => x * 2).Skip(1).Take(2).Distinct()|};
        }
        """);

    [Fact]
    public Task Lambda_NestedTwoDeep_IsUnpacked() => Verify<QueryShape>.AnalyzerAsync("""
        using System;

        static class Sample
        {
            static readonly Func<int, Func<int, Func<int, int>>> Add = a => b => {|S3L0001:c => a + b + c|};
        }
        """);

    [Fact]
    public Task ReturnedObject_OfSixteenLines_IsCreatedFirst() => Verify<ReturnShape>.AnalyzerAsync("""
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

            public static Point Make()
            {
                return {|S3L0002:new Point
                {
                    A = 1,
                    B = 2,
                    C = 3,
                    D = 4,
                    E = 5,
                    F = 6,
                    G = 7,
                    H = 8,
                    I = 9,
                    J = 10,
                    K = 11,
                    L = 12,
                    M = 13
                }|};
            }
        }
        """);

    [Fact]
    public Task ReturnedObject_OfFewLines_StaysInTheReturn() => Verify<ReturnShape>.AnalyzerAsync("""
        class Point
        {
            public int A { get; set; }

            public static Point Make()
            {
                return new Point
                {
                    A = 1
                };
            }
        }
        """);

    [Fact]
    public Task FinalReturn_WithoutABlankLine_InAMethodOfFourStatements_Breathes() => Verify<ReturnShape>.FixAsync<BreatheBeforeReturn>("""
        static class Sample
        {
            static int Sum()
            {
                var a = 1;
                var b = 2;
                var c = 3;
                {|S3L0003:return|} a + b + c;
            }
        }
        """, """
        static class Sample
        {
            static int Sum()
            {
                var a = 1;
                var b = 2;
                var c = 3;

                return a + b + c;
            }
        }
        """);

    [Fact]
    public Task FinalReturn_InAShortMethod_IsLeftAlone() => Verify<ReturnShape>.AnalyzerAsync("""
        static class Sample
        {
            static int Sum()
            {
                var a = 1;
                return a;
            }
        }
        """);

    [Fact]
    public Task ReturnedObject_OfSixteenLines_IsNamedThenReturned() => Verify<ReturnShape>.FixAsync<CreateThenReturn>("""
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

            public static Point Make()
            {
                return {|S3L0002:new Point
                {
                    A = 1,
                    B = 2,
                    C = 3,
                    D = 4,
                    E = 5,
                    F = 6,
                    G = 7,
                    H = 8,
                    I = 9,
                    J = 10,
                    K = 11,
                    L = 12,
                    M = 13
                }|};
            }
        }
        """, """
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

            public static Point Make()
            {
                var point = new Point
                {
                    A = 1,
                    B = 2,
                    C = 3,
                    D = 4,
                    E = 5,
                    F = 6,
                    G = 7,
                    H = 8,
                    I = 9,
                    J = 10,
                    K = 11,
                    L = 12,
                    M = 13
                };

                return point;
            }
        }
        """);

    /// <summary>A member that only builds an object is named for it already; a variable would repeat the name.</summary>
    [Fact]
    public Task ExpressionBody_BuildingALargeObject_IsNotReported() => Verify<ReturnShape>.AnalyzerAsync("""
        sealed class Point
        {
            public int X { get; init; }
            public int Y { get; init; }
            public int Z { get; init; }

            public static Point Origin() => new Point
            {
                X = 0,
                Y = 0,
                Z = 0,
                // 1
                // 2
                // 3
                // 4
                // 5
                // 6
                // 7
                // 8
                // 9
                // 10
                // 11
                // 12
            };
        }
        """);
}
