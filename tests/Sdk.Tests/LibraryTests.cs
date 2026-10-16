namespace Snail.Sdk.Sdk.Tests;

/// <summary>What a library built on Snail.Sdk.Library adds to the base: its public API written down, and native compatibility declared.</summary>
[Collection(SdkFeed.Collection)]
public sealed class LibraryTests(SdkFeed sdk)
{
    private const string Greeting = """
        namespace Library;

        /// <summary>Says hello.</summary>
        public sealed class Greeting(string name)
        {
            /// <summary>The greeting.</summary>
            public string Text() => $"Hello, {name}";
        }
        """;

    private const string PublicApi = """
        #nullable enable
        Library.Greeting
        Library.Greeting.Greeting(string! name) -> void
        Library.Greeting.Text() -> string!

        """;

    /// <summary>A public symbol nobody wrote down in PublicAPI.*.txt fails the build: the API changes only on purpose.</summary>
    [Fact]
    public void Build_OfALibraryWhosePublicApiIsNotWrittenDown_Fails()
    {
        var folder = sdk.Project("Snail.Sdk.Library", "Library", string.Empty, ("Greeting.cs", Greeting));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("RS0016", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_OfALibraryWhosePublicApiIsWrittenDown_Succeeds()
    {
        var folder = sdk.Project("Snail.Sdk.Library", "Library", string.Empty,
            ("Greeting.cs", Greeting),
            ("PublicAPI.Shipped.txt", PublicApi),
            ("PublicAPI.Unshipped.txt", "#nullable enable\n"));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build");

        Assert.True(exitCode == 0, output);
    }

    /// <summary>The reference is written from the built assembly: namespaces, types and members with their summaries.</summary>
    [Fact]
    public void Build_WithSnailReference_WritesTheApiReference()
    {
        var folder = sdk.Project("Snail.Sdk.Library", "Library", string.Empty,
            ("Greeting.cs", Greeting),
            ("PublicAPI.Shipped.txt", PublicApi),
            ("PublicAPI.Unshipped.txt", "#nullable enable\n"));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true");
        var page = File.ReadAllText(Path.Combine(folder, "docs", "reference", "Library.md"));

        Assert.True(exitCode == 0, output);
        Assert.Contains("### class Greeting", page, StringComparison.Ordinal);
        Assert.Contains("Says hello.", page, StringComparison.Ordinal);
        Assert.Contains("| `string Text()` | The greeting. |", page, StringComparison.Ordinal);
    }

    /// <summary>SnailReferenceFolder is the folder of the pages themselves, as on a server: a site with its own layout names it.</summary>
    [Fact]
    public void Build_WithSnailReferenceFolder_WritesThePagesThere()
    {
        var folder = sdk.Project("Snail.Sdk.Library", "Library", """
            <PropertyGroup>
              <SnailReferenceFolder>$(MSBuildProjectDirectory)/docs/content/api/</SnailReferenceFolder>
            </PropertyGroup>
            """,
            ("Greeting.cs", Greeting),
            ("PublicAPI.Shipped.txt", PublicApi),
            ("PublicAPI.Unshipped.txt", "#nullable enable\n"));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true");

        Assert.True(exitCode == 0, output);
        Assert.True(File.Exists(Path.Combine(folder, "docs", "content", "api", "Library.md")), output);
        Assert.False(Directory.Exists(Path.Combine(folder, "docs", "reference")));
    }

    /// <summary>The reference shows the nullable annotations a user compiles against: string? and string are different promises.</summary>
    [Fact]
    public void Build_WithSnailReference_ShowsNullableReferences()
    {
        var folder = sdk.Project("Snail.Sdk.Library", "Library", string.Empty,
            ("Directory.cs", """
                namespace Library;

                /// <summary>Finds people.</summary>
                public sealed class People
                {
                    /// <summary>The nickname, when there is one.</summary>
                    public string? Nickname { get; set; }

                    /// <summary>The first name that starts with the prefix.</summary>
                    public string? Find(string prefix, IReadOnlyList<string?> names, int? limit) => names.FirstOrDefault(name => name?.StartsWith(prefix, StringComparison.Ordinal) == true);
                }
                """),
            ("PublicAPI.Shipped.txt", """
                #nullable enable
                Library.People
                Library.People.People() -> void
                Library.People.Nickname.get -> string?
                Library.People.Nickname.set -> void
                Library.People.Find(string! prefix, System.Collections.Generic.IReadOnlyList<string?>! names, int? limit) -> string?

                """),
            ("PublicAPI.Unshipped.txt", "#nullable enable\n"));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true");
        var page = File.ReadAllText(Path.Combine(folder, "docs", "reference", "Library.md"));

        Assert.True(exitCode == 0, output);
        Assert.Contains("`string? Nickname { get; set; }`", page, StringComparison.Ordinal);
        Assert.Contains("`string? Find(string prefix, IReadOnlyList<string?> names, int? limit)`", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_IsNativeCompatible_UnlessItSaysWhyNot()
    {
        var native = sdk.Project("Snail.Sdk.Library", "Library", string.Empty, ("Greeting.cs", Greeting));
        var notNative = sdk.Project("Snail.Sdk.Library", "Library", """
            <PropertyGroup>
              <SnailNotNativeBecause>its dependency reads wrong under Native AOT</SnailNotNativeBecause>
            </PropertyGroup>
            """, ("Greeting.cs", Greeting));

        var (_, nativeOutput) = SdkFeed.Dotnet(native, "msbuild", "-getProperty:IsAotCompatible", "-getProperty:EnablePackageValidation", "-getProperty:IncludeSymbols");
        var (_, notNativeOutput) = SdkFeed.Dotnet(notNative, "msbuild", "-getProperty:IsAotCompatible");

        Assert.Contains("\"IsAotCompatible\": \"true\"", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("\"EnablePackageValidation\": \"true\"", nativeOutput, StringComparison.Ordinal);
        Assert.Contains("\"IncludeSymbols\": \"true\"", nativeOutput, StringComparison.Ordinal);
        Assert.Equal("false", notNativeOutput.Trim());
    }

    /// <summary>Beside Razor a native library is still checked by the trim and AOT analyzers, though the targets come after Razor's.</summary>
    [Fact]
    public void Library_BesideRazor_IsCheckedByTheNativeAnalyzers()
    {
        var folder = sdk.Project("Microsoft.NET.Sdk.Razor", "Library", """<Sdk Name="Snail.Sdk.Library" />""", ("Greeting.cs", Greeting));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:IsAotCompatible", "-getProperty:EnableTrimAnalyzer", "-getProperty:EnableAotAnalyzer", "-getProperty:_RequiresILLinkPack");

        Assert.True(exitCode == 0, output);
        Assert.Contains("\"EnableTrimAnalyzer\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"EnableAotAnalyzer\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"_RequiresILLinkPack\": \"true\"", output, StringComparison.Ordinal);
    }

    /// <summary>A package of MSBuild targets carries no assembly, and so no symbol package NuGet would refuse as empty.</summary>
    [Fact]
    public void Library_WithoutAnAssembly_CarriesNoSymbols()
    {
        var folder = sdk.Project("Snail.Sdk.Library", "Library", """
            <PropertyGroup>
              <IncludeBuildOutput>false</IncludeBuildOutput>
            </PropertyGroup>
            """);

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:IncludeSymbols");

        Assert.True(exitCode == 0, output);
        Assert.NotEqual("true", output.Trim());
    }

    /// <summary>A build that names no version packs as the last release, not as 1.0.0, which would be below a baseline from 1.0.0 on.</summary>
    [Fact]
    public void Library_BuiltWithoutAVersion_IsVersionedAsItsBaseline()
    {
        var folder = sdk.Project("Snail.Sdk.Library", "Library", """
            <PropertyGroup>
              <SnailPackageBaseline>2.3.0</SnailPackageBaseline>
            </PropertyGroup>
            """, ("Greeting.cs", Greeting));
        var named = sdk.Project("Snail.Sdk.Library", "Library", """
            <PropertyGroup>
              <SnailPackageBaseline>2.3.0</SnailPackageBaseline>
              <VersionPrefix>2.4.0</VersionPrefix>
            </PropertyGroup>
            """, ("Greeting.cs", Greeting));

        var (_, version) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:Version");
        var (_, namedVersion) = SdkFeed.Dotnet(named, "msbuild", "-getProperty:Version");

        Assert.Equal("2.3.0", version.Trim());
        Assert.Equal("2.4.0", namedVersion.Trim());
    }
}
