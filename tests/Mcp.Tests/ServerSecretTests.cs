using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>A secret kept in a file: made once, readable by its owner alone, the same for every process, and never replaced.</summary>
public sealed class ServerSecretTests : IDisposable
{
    private readonly string _folder = TestFolder.Create();

    private string SecretFile => Path.Combine(_folder, "data", "proof.key");

    [Fact]
    public void ReadOrCreate_FirstUse_MakesASecretOnlyItsOwnerReads()
    {
        var secret = ServerSecret.ReadOrCreate(SecretFile);

        Assert.Equal(32, secret.Length);
        Assert.Equal(secret, File.ReadAllBytes(SecretFile));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SecretFile)!));

        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SecretFile));
    }

    [Fact]
    public void ReadOrCreate_Again_IsTheSameSecret()
    {
        var first = ServerSecret.ReadOrCreate(SecretFile, 48);

        var second = ServerSecret.ReadOrCreate(SecretFile, 48);

        Assert.Equal(48, first.Length);
        Assert.Equal(first, second);
    }

    /// <summary>Every server of a machine may start at once: one secret stands, and nothing is left beside it.</summary>
    [Fact]
    public async Task ReadOrCreate_SixteenAtOnce_AgreeOnOneSecretAndLeaveOneFile()
    {
        var reads = Enumerable.Range(0, 16).Select(_ => Task.Run(() => ServerSecret.ReadOrCreate(SecretFile)));

        var secrets = await Task.WhenAll(reads);

        Assert.All(secrets, secret => Assert.Equal(secrets[0], secret));
        Assert.Equal(secrets[0], File.ReadAllBytes(SecretFile));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SecretFile)!));
    }

    /// <summary>A server logs "wrote a new token" once, from what the call says, not from a look at the file that races with the others.</summary>
    [Fact]
    public void ReadOrCreate_SaysWhetherItMadeTheSecret()
    {
        var first = ServerSecret.ReadOrCreate(SecretFile, 32, out var isMade);
        var second = ServerSecret.ReadOrCreate(SecretFile, 32, out var isMadeAgain);

        Assert.True(isMade);
        Assert.False(isMadeAgain);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ReadOrCreateHex_SaysWhetherItMadeTheSecret()
    {
        var first = ServerSecret.ReadOrCreateHex(SecretFile, 16, out var isMade);
        var second = ServerSecret.ReadOrCreateHex(SecretFile, 16, out var isMadeAgain);

        Assert.True(isMade);
        Assert.False(isMadeAgain);
        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
    }

    /// <summary>Of sixteen servers starting at once, exactly one is told it made the secret.</summary>
    [Fact]
    public async Task ReadOrCreate_SixteenAtOnce_TellOneOfThemItMadeTheSecret()
    {
        var reads = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            ServerSecret.ReadOrCreate(SecretFile, 32, out var isMade);

            return isMade;
        }));

        var made = await Task.WhenAll(reads);

        Assert.Single(made, isMade => isMade);
    }

    /// <summary>A file cut short is not taken as the secret, and not replaced by a new one that would invalidate what was handed out.</summary>
    [Fact]
    public void ReadOrCreate_AFileOfAnotherLength_IsRefusedAndKept()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretFile)!);
        File.WriteAllBytes(SecretFile, [1, 2, 3]);

        var refusal = Assert.Throws<ServerSecretRefusedException>(() => ServerSecret.ReadOrCreate(SecretFile));

        Assert.Contains(SecretFile, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("3 bytes instead of 32 bytes", refusal.Message, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(SecretFile));
    }

    /// <summary>#137: a secret is read at the start, so its refusal is the start refusal a program says in one line.</summary>
    [Fact]
    public void ReadOrCreate_AFileOfAnotherLength_IsAStartRefusal()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretFile)!);
        File.WriteAllBytes(SecretFile, [1, 2, 3]);

        var refusal = Assert.ThrowsAny<StartRefusedException>(() => ServerSecret.ReadOrCreate(SecretFile));

        Assert.IsType<ServerSecretRefusedException>(refusal);
    }

    [Fact]
    public void ReadOrCreate_AShortSecret_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ServerSecret.ReadOrCreate(SecretFile, 8));

    [Fact]
    public void ReadOrCreateHex_FirstUse_WritesTheDigitsAsText()
    {
        var token = ServerSecret.ReadOrCreateHex(SecretFile);

        Assert.Equal(64, token.Length);
        Assert.Equal(token, File.ReadAllText(SecretFile));
        Assert.All(token, digit => Assert.True(char.IsAsciiHexDigit(digit)));
        Assert.Equal(token, ServerSecret.ReadOrCreateHex(SecretFile));
    }

    /// <summary>Snail.MCP.Figma's bridge.token, written by hand or by an older server with a line end, is read as it is.</summary>
    [Fact]
    public void ReadOrCreateHex_AFileKeptBefore_IsReadAsItIs()
    {
        var kept = new string('a', 64);
        Directory.CreateDirectory(Path.GetDirectoryName(SecretFile)!);
        File.WriteAllText(SecretFile, $"{kept}\n");

        Assert.Equal(kept, ServerSecret.ReadOrCreateHex(SecretFile));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void ReadOrCreateHex_AFileThatIsNotTheDigits_IsRefusedAndKept(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretFile)!);
        File.WriteAllText(SecretFile, text);

        Assert.Throws<ServerSecretRefusedException>(() => ServerSecret.ReadOrCreateHex(SecretFile));
        Assert.Equal(text, File.ReadAllText(SecretFile));
    }

    /// <summary>A server adds its own consequence to the refusal from what it says, never from its message.</summary>
    [Fact]
    public void ReadOrCreateHex_ARefusal_SaysTheFileWhatItHoldsAndWhatWasAsked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SecretFile)!);
        File.WriteAllText(SecretFile, "abc");

        var refusal = Assert.Throws<ServerSecretRefusedException>(() => ServerSecret.ReadOrCreateHex(SecretFile));

        Assert.Equal(SecretFile, refusal.File);
        Assert.Equal("3 bytes", refusal.Found);
        Assert.Equal("64 hex digits", refusal.Expected);
        Assert.IsAssignableFrom<InvalidOperationException>(refusal);
    }

    [Fact]
    public void Matches_TheSameBytes_Only()
    {
        byte[] secret = [1, 2, 3, 4];

        Assert.True(ServerSecret.Matches(secret, [1, 2, 3, 4]));
        Assert.False(ServerSecret.Matches(secret, [1, 2, 3, 5]));
        Assert.False(ServerSecret.Matches(secret, [1, 2, 3]));
        Assert.False(ServerSecret.Matches(secret, []));
    }

    [Fact]
    public void Matches_TheSameText_Only()
    {
        Assert.True(ServerSecret.Matches("c0ffee", "c0ffee"));
        Assert.False(ServerSecret.Matches("c0ffee", "C0FFEE"));
        Assert.False(ServerSecret.Matches("c0ffee", "c0ffe"));
        Assert.False(ServerSecret.Matches("c0ffee", null));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
