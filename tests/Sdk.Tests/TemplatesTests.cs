using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Snail.Sdk.Sdk.Tests;

/// <summary>A project made from a template passes what the shared CI asks of it, with no line changed.</summary>
/// <remarks>
/// Conforming, built, tested, and its reference written again by the build without a difference from the one the template carries.
/// The servers cover the template's conditional code between them: the default (native, stdio) also published with
/// Native AOT and answering its contract as that program, HTTP alone, both transports framework-dependent, and the
/// container with its Dockerfile and image workflow.
/// </remarks>
[Collection(SdkFeed.Collection)]
public sealed class TemplatesTests(SdkFeed sdk)
{
    /// <summary>The library passes the shared CI and packs, with its symbols, as lib-ci.yml packs it.</summary>
    [Fact]
    public void Library_FromTheTemplate_PassesTheSharedCiAndPacks()
    {
        var folder = sdk.FromTemplate("s3l-lib", "Snail.Toolkit.Sample");

        AssertPassesTheSharedCi(folder);

        var packed = Path.Combine(folder, "packed");
        var (exitCode, output) = SdkFeed.Dotnet(folder, "pack", "-c", "Release", "--no-build", "-o", packed);

        Assert.True(exitCode == 0, output);
        Assert.Single(Directory.EnumerateFiles(packed, "Snail.Toolkit.Sample.*.nupkg"));
        Assert.Single(Directory.EnumerateFiles(packed, "Snail.Toolkit.Sample.*.snupkg"));
    }

    /// <summary>The default server — native, over stdio — passes the shared CI and, published with Native AOT, answers its contract as that program.</summary>
    [Fact]
    public void Server_FromTheTemplateByDefault_PassesTheSharedCiAndAnswersAsPublishedNatively()
    {
        var folder = sdk.FromTemplate("s3l-mcp", "Snail.MCP.Sample");

        AssertPassesTheSharedCi(folder);

        var published = Path.Combine(folder, "published");
        var (exitCode, output) = SdkFeed.Dotnet(folder, "publish", Path.Combine("src", "Snail.MCP.Sample.csproj"), "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier, "-o", published);
        var program = Path.Combine(published, OperatingSystem.IsWindows() ? "Snail.MCP.Sample.exe" : "Snail.MCP.Sample");
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["SNAIL_SAMPLE_SERVER"] = program };
        var (testedExitCode, testOutput) = SdkFeed.Dotnet(folder, environment, "test", "-c", "Release", "--filter", "FullyQualifiedName~Contract");

        Assert.True(exitCode == 0, output);
        Assert.DoesNotMatch(@"warning (IL|AOT)\d+", output);
        Assert.False(File.Exists(Path.Combine(published, "Snail.MCP.Sample.dll")), "the publication is not native");
        Assert.True(testedExitCode == 0, testOutput);
        Assert.False(File.Exists(Path.Combine(folder, "Dockerfile")));
        Assert.False(File.Exists(Path.Combine(folder, ".github", "workflows", "image.yml")));
        Assert.False(Directory.Exists(Path.Combine(folder, "deploy")));
        Assert.False(Directory.Exists(Path.Combine(folder, "src", "Resources")), "localization is asked for, not given");
    }

    [Fact]
    public void Server_FromTheTemplateOverHttp_PassesTheSharedCi()
    {
        var folder = sdk.FromTemplate("s3l-mcp", "Snail.MCP.Sample", "--transport", "Http");

        AssertPassesTheSharedCi(folder);
        Assert.True(File.Exists(Path.Combine(folder, "deploy", "s3l.mcp.sample.plist")));
        Assert.False(File.Exists(Path.Combine(folder, "tests", "StdioContractTests.cs")));
    }

    [Fact]
    public void Server_FromTheTemplateWithBothTransportsNotNative_PassesTheSharedCi() =>
        AssertPassesTheSharedCi(sdk.FromTemplate("s3l-mcp", "Snail.MCP.Sample", "--transport", "Both", "--native", "false"));

    /// <summary>A container server conforms: its Dockerfile publishes natively onto runtime-deps, on the SDK band of global.json, and image.yml builds it.</summary>
    [Fact]
    public void Server_FromTheTemplateInAContainer_PassesTheSharedCiWithANativeImage()
    {
        var folder = sdk.FromTemplate("s3l-mcp", "Snail.MCP.Sample", "--container", "true");

        AssertPassesTheSharedCi(folder);

        var dockerfile = File.ReadAllText(Path.Combine(folder, "Dockerfile"));

        Assert.Contains($"FROM mcr.microsoft.com/dotnet/sdk:{SdkBand(folder)} AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("clang zlib1g-dev", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet publish src/Snail.MCP.Sample.csproj -c Release -r linux-x64 -o /app", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/runtime-deps:10.0", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"/app/Snail.MCP.Sample\"]", dockerfile, StringComparison.Ordinal);
        Assert.Contains("docker build -t s3l-sample-mcp:local .", dockerfile, StringComparison.Ordinal);
        Assert.Contains("docker run -i --rm s3l-sample-mcp:local", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ARG VERSION=0.0.0-local", dockerfile, StringComparison.Ordinal);
        Assert.Contains("-p:Version=$VERSION", dockerfile, StringComparison.Ordinal);
        Assert.Contains("LABEL org.opencontainers.image.version=$VERSION", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("#if", dockerfile, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, ".github", "workflows", "image.yml")));
        Assert.True(File.Exists(Path.Combine(folder, ".dockerignore")));
    }

    /// <summary>A container server that is not native publishes framework-dependent onto the ASP.NET runtime.</summary>
    [Fact]
    public void Server_FromTheTemplateInAContainerNotNative_HasAFrameworkDependentImage()
    {
        var folder = sdk.FromTemplate("s3l-mcp", "Snail.MCP.Sample", "--container", "true", "--native", "false", "--transport", "Http");

        var dockerfile = File.ReadAllText(Path.Combine(folder, "Dockerfile"));

        Assert.Contains($"FROM mcr.microsoft.com/dotnet/sdk:{SdkBand(folder)} AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("-p:PublishAot=false", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:10.0", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"/app/Snail.MCP.Sample.dll\"]", dockerfile, StringComparison.Ordinal);
        Assert.Contains("SNAIL_MCP_SAMPLE_URL", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("clang", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("docker run -i", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("#if", dockerfile, StringComparison.Ordinal);
    }

    /// <summary>
    /// The default application — a minimal API, composed behind traefik — passes the shared CI as an application, and
    /// its image starts the application under tini, so a failed start ends the container instead of spinning in it.
    /// </summary>
    [Fact]
    public void Application_FromTheTemplateByDefault_PassesTheSharedCi()
    {
        var folder = sdk.FromTemplate("s3l-web", "Vertical.Sample");

        AssertPassesTheSharedCi(folder);

        var dockerfile = File.ReadAllText(Path.Combine(folder, "Dockerfile"));

        Assert.Contains($"FROM mcr.microsoft.com/dotnet/sdk:{SdkBand(folder)} AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet publish src/Vertical.Sample.csproj -c Release -o /app -p:UseAppHost=false -p:Version=$VERSION", dockerfile, StringComparison.Ordinal);
        Assert.Contains("ENTRYPOINT [\"/usr/bin/tini\", \"--\", \"dotnet\", \"/app/Vertical.Sample.dll\"]", dockerfile, StringComparison.Ordinal);
        Assert.Contains("docker build -t vertical-sample:local .", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("node", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("#if", dockerfile, StringComparison.Ordinal);
        Assert.Contains("traefik.http.routers.vertical-sample.rule", File.ReadAllText(Path.Combine(folder, "docker-compose.yml")), StringComparison.Ordinal);
        Assert.Contains("image: vertical-sample", File.ReadAllText(Path.Combine(folder, ".github", "workflows", "image.yml")), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(folder, "deploy")));
    }

    /// <summary>
    /// Blazor pages styled with Tailwind, in English, Russian and Spanish, with a sign-in cookie and MongoDB: the build
    /// stage of the image brings Node for Tailwind.
    /// </summary>
    [Fact]
    public void Application_FromTheTemplateAsLocalizedPagesWithACookieAndMongo_PassesTheSharedCi()
    {
        var folder = sdk.FromTemplate("s3l-web", "Vertical.Pages", "--kind", "blazor", "--auth", "cookie", "--mongo", "true", "--localization", "true");

        AssertPassesTheSharedCi(folder);

        var dockerfile = File.ReadAllText(Path.Combine(folder, "Dockerfile"));
        var compose = File.ReadAllText(Path.Combine(folder, "docker-compose.yml"));

        Assert.True(File.Exists(Path.Combine(folder, "src", "wwwroot", "css", "app.css")), "Tailwind wrote no stylesheet");
        Assert.Contains("FROM node:22-bookworm-slim AS node", dockerfile, StringComparison.Ordinal);
        Assert.Contains("DataProtection__KeysPath=/keys", dockerfile, StringComparison.Ordinal);
        Assert.Contains("Mongodb__Connection: mongodb://mongo:27017", compose, StringComparison.Ordinal);
        Assert.Contains("- keys:/keys", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("#if", compose, StringComparison.Ordinal);
        Assert.Equal(["Messages.es.resx", "Messages.resx", "Messages.ru.resx"],
            Directory.EnumerateFiles(Path.Combine(folder, "src", "Resources")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("#if", File.ReadAllText(Path.Combine(folder, "src", "Resources", "Messages.ru.resx")), StringComparison.Ordinal);
        Assert.DoesNotContain("#if", File.ReadAllText(Path.Combine(folder, "src", "Components", "App.razor")), StringComparison.Ordinal);
    }

    /// <summary>A minimal API answering in the client's language, with a bearer token, RabbitMQ and Vault, run as a Nomad job instead of composed.</summary>
    [Fact]
    public void Application_FromTheTemplateWithJwtRabbitAndVaultOnNomad_PassesTheSharedCi()
    {
        var folder = sdk.FromTemplate("s3l-web", "Vertical.Gateway", "--auth", "jwt", "--rabbit", "true", "--vault", "true", "--deploy", "nomad", "--localization", "true");

        AssertPassesTheSharedCi(folder);

        var job = File.ReadAllText(Path.Combine(folder, "deploy", "nomad", "vertical-gateway.nomad.hcl"));

        Assert.Contains("image = \"ghcr.io/orldev/vertical-gateway:latest\"", job, StringComparison.Ordinal);
        Assert.Contains("Jwt__SecretKey", job, StringComparison.Ordinal);
        Assert.DoesNotContain("#if", job, StringComparison.Ordinal);
        Assert.Contains("AddVaultWhenConfigured", File.ReadAllText(Path.Combine(folder, "src", "Program.cs")), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "docker-compose.yml")));
    }

    private static void AssertPassesTheSharedCi(string folder)
    {
        Git(folder, "init", "-q");
        Git(folder, "add", "-A");
        Git(folder, "-c", "user.name=test", "-c", "user.email=test@example.com", "commit", "-q", "-m", "template");

        var project = Directory.EnumerateFiles(Path.Combine(folder, "src"), "*.csproj", SearchOption.AllDirectories).Order(StringComparer.Ordinal).First();
        var (conforming, conformanceOutput) = SdkFeed.Dotnet(folder, "msbuild", project, "-t:SnailConformance", "-nologo");
        var (built, buildOutput) = SdkFeed.Dotnet(folder, "build", "-c", "Release", "-p:SnailReference=true");
        var (tested, testOutput) = SdkFeed.Dotnet(folder, "test", "-c", "Release", "--no-build");
        var changed = Git(folder, "status", "--porcelain", "--", "docs");

        Assert.True(conforming == 0, conformanceOutput);
        Assert.True(built == 0, buildOutput);
        Assert.Contains("0 Warning(s)", buildOutput, StringComparison.Ordinal);
        Assert.True(tested == 0, testOutput);
        Assert.Equal(string.Empty, changed);
    }

    /// <summary>The SDK version global.json pins: the band an image's build stage has to name.</summary>
    private static string SdkBand(string folder)
    {
        using var globalJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "global.json")));

        return globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
    }

    private static string Git(string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = folder, RedirectStandardOutput = true };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var git = Process.Start(start)!;
        var output = git.StandardOutput.ReadToEnd();

        git.WaitForExit();

        return output.Trim();
    }
}
