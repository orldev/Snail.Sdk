namespace Snail.Sdk.Analyzers.Tests;

public sealed class NamingTests
{
    [Fact]
    public Task Type_NamedForAPattern_IsReported() => Verify<Naming>.AnalyzerAsync("""
        sealed class {|S3L0006:OrderService|}
        {
        }
        """);

    [Fact]
    public Task Namespace_NamedHelpers_IsReported() => Verify<Naming>.AnalyzerAsync("""
        namespace {|S3L0006:Sample.Helpers|};

        sealed class Orders
        {
        }
        """);

    [Fact]
    public Task Local_NamedForItsListType_IsReported() => Verify<Naming>.AnalyzerAsync("""
        using System.Collections.Generic;

        static class Sample
        {
            static int Count()
            {
                List<int> {|S3L0007:userList|} = [];
                return userList.Count;
            }
        }
        """);

    [Fact]
    public Task Local_NamedForWhatItHolds_IsLeftAlone() => Verify<Naming>.AnalyzerAsync("""
        using System.Collections.Generic;

        static class Sample
        {
            static int Count()
            {
                List<int> users = [];
                return users.Count;
            }
        }
        """);

    /// <summary>A released name changes only in a major version, so the naming rules leave it alone and judge what is new.</summary>
    [Fact]
    public Task Name_AlreadyShipped_IsNotReported() => Verify<Naming>.AnalyzerAsync("""
        namespace Library;

        public sealed class PptxOptions
        {
            public bool EmbedFonts { get; set; }
            public bool {|S3L0008:Compress|} { get; set; }
        }

        public sealed class FontHelper;
        """, shippedApi: """
        #nullable enable
        Library.FontHelper
        Library.FontHelper.FontHelper() -> void
        Library.PptxOptions
        Library.PptxOptions.EmbedFonts.get -> bool
        Library.PptxOptions.EmbedFonts.set -> void
        """);

    /// <summary>A shipped line opens with its modifiers — static, const, override — and spells type arguments; the name is found all the same.</summary>
    [Fact]
    public Task Name_ShippedStaticOrGeneric_IsNotReported() => Verify<Naming>.AnalyzerAsync("""
        namespace Library;

        public static class Defaults<T>
        {
            public static bool Compress { get; set; }
            public static bool {|S3L0008:Encrypt|} { get; set; }
        }
        """, shippedApi: """
        #nullable enable
        Library.Defaults<T>
        static Library.Defaults<T>.Compress.get -> bool
        static Library.Defaults<T>.Compress.set -> void
        """);

    [Fact]
    public Task Boolean_OpeningWithAThirdPersonVerb_Asks() => Verify<Naming>.AnalyzerAsync("""
        public sealed class Flow
        {
            public bool ForcesBreakBefore { get; set; }
            public bool AvoidsBreakInside { get; set; }
            public bool PreservesLineFeeds { get; set; }
            public bool {|S3L0008:Status|} { get; set; }
            public bool {|S3L0008:Class|} { get; set; }
        }
        """);

    [Fact]
    public Task Boolean_ThatDoesNotAsk_IsReported() => Verify<Naming>.AnalyzerAsync("""
        sealed class Order
        {
            public bool {|S3L0008:Ready|} { get; set; }

            public bool IsPaid { get; set; }

            public bool TryShip(out int parcel)
            {
                parcel = 1;
                return true;
            }

            public override bool Equals(object? other) => ReferenceEquals(this, other);

            public override int GetHashCode() => 1;
        }
        """);
}
