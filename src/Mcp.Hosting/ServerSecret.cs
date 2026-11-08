using System.Security.Cryptography;
using System.Text;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>A secret the server keeps in a file of its own — a key it signs with, a token its plugin presents — made once and never replaced.</summary>
/// <remarks>
/// <para>
/// The secret is made the first time it is asked for and kept: a new one would invalidate whatever was handed out with
/// the old — a token stamped into an installed plugin, a code a customer published. So a file of another length than the
/// secret's is refused, never written over: it is someone's to look at.
/// </para>
/// <para>
/// The file is readable by its owner alone from the moment it exists (0600), and appears only whole: the secret is
/// written to a draft of its own, flushed to the disk and then moved into place. Every server of a machine may start at
/// once and ask for the same secret, so the move is made by one of them at a time, under a lock file beside the secret,
/// and the others read the secret it put there. <c>File.Move</c> without overwriting does not suffice on its own: on Unix
/// it looks for the target and then renames over it, so two servers could each move a secret of their own and the second
/// replace the first after it was handed out — Snail.MCP.Figma saw two tokens one run in three before it stopped relying
/// on it.
/// </para>
/// </remarks>
public static class ServerSecret
{
    /// <summary>The shortest secret made: 128 bits.</summary>
    internal const int ShortestLength = 16;

    /// <summary>The length of a secret made when none is named: 256 bits.</summary>
    private const int DefaultLength = 32;

    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(10);

    /// <summary>The secret of <paramref name="length"/> random bytes kept in <paramref name="file"/>, made the first time it is asked for.</summary>
    /// <param name="file">The file it is kept in, such as <c>proof.key</c> in <see cref="ServerFolders.DataDirectory(string)"/>.</param>
    /// <param name="length">How many bytes it is; 16 at the least.</param>
    /// <exception cref="ServerSecretRefusedException">The file holds another number of bytes; it is left as it is.</exception>
    /// <exception cref="IOException">Another process held the lock beside the file for ten seconds.</exception>
    public static byte[] ReadOrCreate(string file, int length) => ReadOrCreate(file, length, out _);

    /// <summary>The secret of 32 random bytes kept in <paramref name="file"/>, made the first time it is asked for.</summary>
    /// <param name="file">The file it is kept in.</param>
    /// <exception cref="ServerSecretRefusedException">The file holds another number of bytes; it is left as it is.</exception>
    /// <exception cref="IOException">Another process held the lock beside the file for ten seconds.</exception>
    public static byte[] ReadOrCreate(string file) => ReadOrCreate(file, DefaultLength, out _);

    /// <summary>The secret of <paramref name="length"/> random bytes kept in <paramref name="file"/>, and whether this call made it.</summary>
    /// <remarks>
    /// For a server that says so in its log — "wrote a new token, hand it to the plugin again" — where looking for the file
    /// first raced with every other server of the machine making it at once: the one that made the file is the one told.
    /// </remarks>
    /// <param name="file">The file it is kept in.</param>
    /// <param name="length">How many bytes it is; 16 at the least.</param>
    /// <param name="created">True when this call wrote the file; false when the secret was there already, or another process wrote it first.</param>
    /// <exception cref="ServerSecretRefusedException">The file holds another number of bytes; it is left as it is.</exception>
    /// <exception cref="IOException">Another process held the lock beside the file for ten seconds.</exception>
    public static byte[] ReadOrCreate(string file, int length, out bool created)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, ShortestLength);

        var kept = Kept(file, () => RandomNumberGenerator.GetBytes(length), out created);

        if (kept.Length != length)
            throw new ServerSecretRefusedException(file, $"{kept.Length} bytes", $"{length} bytes");

        return kept;
    }

    /// <summary>The same secret written as lower-case hex digits, for a file another program reads as text.</summary>
    /// <param name="file">The file it is kept in, as text: two hex digits a byte.</param>
    /// <param name="length">How many bytes it is; the file holds twice as many digits.</param>
    /// <remarks>
    /// For a secret that leaves the server as text: stamped into a plugin's page (Snail.MCP.Figma's <c>bridge.token</c>),
    /// read from the file by a script inside another program (Snail.MCP.AfterEffects' bridge). Both keep such files
    /// already, so these files are read as they are: white space around the digits is allowed.
    /// </remarks>
    /// <exception cref="ServerSecretRefusedException">The file holds anything but the digits of a secret of that length; it is left as it is.</exception>
    /// <exception cref="IOException">Another process held the lock beside the file for ten seconds.</exception>
    public static string ReadOrCreateHex(string file, int length) => ReadOrCreateHex(file, length, out _);

    /// <summary>The secret of 32 random bytes written as lower-case hex digits, for a file another program reads as text.</summary>
    /// <param name="file">The file it is kept in, as text: two hex digits a byte.</param>
    /// <exception cref="ServerSecretRefusedException">The file holds anything but the digits of a secret of that length; it is left as it is.</exception>
    /// <exception cref="IOException">Another process held the lock beside the file for ten seconds.</exception>
    public static string ReadOrCreateHex(string file) => ReadOrCreateHex(file, DefaultLength, out _);

    /// <summary>The same secret written as lower-case hex digits, and whether this call made it.</summary>
    /// <param name="file">The file it is kept in, as text: two hex digits a byte.</param>
    /// <param name="length">How many bytes it is; the file holds twice as many digits.</param>
    /// <param name="created">True when this call wrote the file; false when the secret was there already, or another process wrote it first.</param>
    /// <exception cref="ServerSecretRefusedException">The file holds anything but the digits of a secret of that length; it is left as it is.</exception>
    /// <exception cref="IOException">Another process held the lock beside the file for ten seconds.</exception>
    public static string ReadOrCreateHex(string file, int length, out bool created)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, ShortestLength);

        var kept = Kept(file, () => Encoding.ASCII.GetBytes(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(length))), out created);
        var text = Encoding.ASCII.GetString(kept).Trim();
        var isWhole = text.Length == length * 2 && text.All(char.IsAsciiHexDigit);

        if (!isWhole)
            throw new ServerSecretRefusedException(file, $"{kept.Length} bytes", $"{length * 2} hex digits");

        return text;
    }

    /// <summary>Whether the presented secret is the expected one, compared in constant time.</summary>
    /// <remarks>The time it takes tells nothing of how much of the secret was right; a secret of another length is refused at once.</remarks>
    public static bool Matches(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> presented) =>
        CryptographicOperations.FixedTimeEquals(expected, presented);

    /// <summary>Whether the presented text is the expected secret, compared in constant time; nothing presented matches nothing.</summary>
    public static bool Matches(string expected, string? presented)
    {
        ArgumentNullException.ThrowIfNull(expected);

        return presented is not null && Matches(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
    }

    /// <summary>The bytes of the file, which one process at a time makes from <paramref name="fresh"/> when it is missing.</summary>
    /// <param name="file">The file the secret is kept in.</param>
    /// <param name="fresh">The bytes of a new secret.</param>
    /// <param name="created">Whether this process published the file.</param>
    private static byte[] Kept(string file, Func<byte[]> fresh, out bool created)
    {
        var path = Path.GetFullPath(file);

        created = !File.Exists(path) && TryMakeOnce(path, fresh);

        return File.ReadAllBytes(path);
    }

    /// <summary>Makes the file under the lock beside it, unless another process made it first; true when this one made it.</summary>
    /// <remarks>
    /// The lock file is removed once the secret stands. A process that still waits on the removed lock, or opens a new
    /// one, finds the secret there and makes nothing: the lock only ever guards a secret that does not exist yet.
    /// </remarks>
    private static bool TryMakeOnce(string file, Func<byte[]> fresh)
    {
        var folder = Path.GetDirectoryName(file)!;
        var lockFile = $"{file}.lock";
        var isMade = false;

        Directory.CreateDirectory(folder);

        using (Locked(lockFile))
        {
            if (!File.Exists(file))
            {
                Publish(file, fresh());
                isMade = true;
            }
        }

        TryDelete(lockFile);

        return isMade;
    }

    /// <summary>The lock beside the secret, held by this process alone; another holding it is waited for.</summary>
    private static FileStream Locked(string lockFile)
    {
        var waited = TimeSpan.Zero;

        while (true)
        {
            try
            {
                return new FileStream(lockFile, OwnerOnly(FileMode.OpenOrCreate, FileShare.None));
            }
            catch (IOException) when (waited < LongestWait)
            {
                Thread.Sleep(Retry);
                waited += Retry;
            }
        }
    }

    /// <summary>Writes the secret to a draft only its owner reads, flushed to the disk, and moves the draft into place whole.</summary>
    private static void Publish(string file, byte[] secret)
    {
        var draft = $"{file}.{Guid.NewGuid():N}.new";

        try
        {
            using (var stream = new FileStream(draft, OwnerOnly(FileMode.CreateNew, FileShare.None)))
            {
                stream.Write(secret);
                stream.Flush(flushToDisk: true);
            }

            File.Move(draft, file, overwrite: false);
        }
        finally
        {
            File.Delete(draft);
        }
    }

    private static FileStreamOptions OwnerOnly(FileMode mode, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = share };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        return options;
    }

    private static void TryDelete(string lockFile)
    {
        try
        {
            File.Delete(lockFile);
        }
        catch (Exception held) when (held is IOException or UnauthorizedAccessException)
        {
            return;
        }
    }
}
