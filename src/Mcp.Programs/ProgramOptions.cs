using System.Globalization;
using System.Text.RegularExpressions;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>How <c>run_program</c> of a server is named to a program and how far a program may go.</summary>
/// <remarks>
/// The bounds are the ones three servers of the fleet arrived at separately (Snail.MCP.Git 0c91672, Snail.MCP.AfterEffects
/// 2d3a67a, Snail.MCP.Blender 528eb3c): a program is a cheaper way to spend one call, not a way to hide a thousand, and
/// every bound stops it with words that say which bound and what to change. The tool's description states them, written
/// from these values, so it cannot drift from them.
/// </remarks>
public sealed partial class ProgramOptions
{
    /// <summary>The name of the object the commands hang on: <c>git</c> for <c>git.status()</c>; <c>tools</c> unless set.</summary>
    /// <remarks>
    /// A JavaScript identifier, and neither <c>log</c> nor <c>input</c>, which are the program's own. With
    /// <see cref="ToolCommands"/>, a tool named <c>&lt;global&gt;_&lt;verb&gt;</c> is the command <c>&lt;verb&gt;</c>.
    /// </remarks>
    public string Global { get; set; } = "tools";

    /// <summary>How long a program runs when the call names no time; two minutes unless set.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The longest a call may give a program; ten minutes unless set, and never less than <see cref="Timeout"/>.</summary>
    /// <remarks>
    /// A <see cref="Timeout"/> set past it raises it to the same value when the tool is registered: a server that gives
    /// its programs twenty minutes means a call may have them too, and refusing to start said nothing it did not know.
    /// A ceiling of zero or less still fails at startup.
    /// </remarks>
    public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The memory a program may allocate; 32 MiB unless set.</summary>
    public long MemoryBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>How many commands one program may send; 200 unless set.</summary>
    /// <remarks>Snail.MCP.Blender and Snail.MCP.AfterEffects allow 200; Snail.MCP.Git, whose commands each start git, 100.</remarks>
    public int MaxCalls { get; set; } = 200;

    /// <summary>How deep a program's calls may nest; 256 unless set.</summary>
    /// <remarks>Unbounded, a recursion that never ends is stopped by the thread's stack, and a stack overflow ends the server, not the program.</remarks>
    public int MaxRecursion { get; set; } = 256;

    /// <summary>How many statements a program may run; a million unless set.</summary>
    /// <remarks>The timeout alone lets a busy loop burn a core for as long as it was given; this ends <c>while (true) {}</c> in about 50 ms.</remarks>
    public int MaxStatements { get; set; } = 1_000_000;

    /// <summary>How many lines <c>log(…)</c> keeps; 200 unless set. Lines past it are dropped.</summary>
    public int LogLines { get; set; } = 200;

    /// <summary>How long a log line may be; 1000 characters unless set. A longer one is cut and ends in an ellipsis.</summary>
    public int LogLineLength { get; set; } = 1000;

    /// <summary>The largest result an answer carries, in bytes of JSON; 128 KiB unless set.</summary>
    /// <remarks>A larger result is left out with a note saying so: a program that returned <c>'x'.repeat(1000000)</c> answered a megabyte before Snail.MCP.Git held it to its budget.</remarks>
    public int ResultBytes { get; set; } = 128 * 1024;

    /// <summary>What a program of this server is for, written ahead of the rules and limits the tool's description always states; a general sentence unless set.</summary>
    public string? Description { get; set; }

    /// <summary>Called when a program of <c>run_program</c> has run, finished or stopped, with what it did and the services of its request; nothing unless set.</summary>
    /// <remarks>
    /// <para>
    /// For what a server counts or records of its programs: Snail.MCP.Blender's traffic report counted a program at the
    /// first command its catalog was sent — a program that sent none went uncounted, and a catalog had to keep state to
    /// tell the first command from the rest. Here the whole run is in hand: its commands, its result, why it stopped.
    /// </para>
    /// <para>
    /// Called once per run, before the answer is written, on the thread that writes it; not for a call refused before
    /// anything ran (no code, a timeout out of range). It runs inside the call, so it should be quick, and an exception
    /// it throws fails the call — after the program's commands, which stand either way.
    /// </para>
    /// </remarks>
    public Action<ProgramRun, IServiceProvider>? Ran { get; set; }

    /// <summary>The bounds in the words the tool's description gives them.</summary>
    internal string Limits =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Limits: {MaxCalls} commands, {MaxStatements} statements, calls nested {MaxRecursion} deep, {MemoryBytes / 1024 / 1024} MB of memory, and {Seconds(Timeout)} seconds unless timeoutSeconds gives up to {Seconds(MaxTimeout)}.");

    /// <summary>A duration in whole seconds, as the tool takes and states it.</summary>
    internal static int Seconds(TimeSpan duration) => (int)Math.Ceiling(duration.TotalSeconds);

    /// <summary>Throws when a value cannot work, a registration that would fail on every call failing at startup instead; raises <see cref="MaxTimeout"/> to <see cref="Timeout"/>.</summary>
    internal void Check()
    {
        if (!Identifier().IsMatch(Global) || Global is "log" or "input")
            throw new ArgumentException($"'{Global}' cannot name the commands' object: it must be a JavaScript identifier other than log and input", nameof(Global));

        ArgumentOutOfRangeException.ThrowIfLessThan(Timeout, TimeSpan.FromSeconds(1), nameof(Timeout));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(MaxTimeout, TimeSpan.Zero, nameof(MaxTimeout));

        if (MaxTimeout < Timeout)
            MaxTimeout = Timeout;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MemoryBytes, nameof(MemoryBytes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCalls, nameof(MaxCalls));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxRecursion, nameof(MaxRecursion));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStatements, nameof(MaxStatements));
        ArgumentOutOfRangeException.ThrowIfNegative(LogLines, nameof(LogLines));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(LogLineLength, nameof(LogLineLength));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ResultBytes, nameof(ResultBytes));
    }

    [GeneratedRegex("^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex Identifier();
}
