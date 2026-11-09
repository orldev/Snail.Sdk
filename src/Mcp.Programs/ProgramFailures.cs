using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>The kinds of stop a program reports as its <c>type</c>, words a client can branch on.</summary>
/// <remarks>A command that failed and was not caught keeps its own type instead, so the reply says what failed rather than only that a program stopped (Snail.MCP.Blender).</remarks>
public static class ProgramFailures
{
    /// <summary>The program ran longer than it was given.</summary>
    public const string Timeout = "timeout";

    /// <summary>The call that ran the program was cancelled.</summary>
    public const string Cancelled = Snail.Sdk.Mcp.Hosting.ToolFailures.Cancelled;

    /// <summary>The program nested its calls too deep, ran too many statements or held too much memory.</summary>
    public const string TooLarge = "programTooLarge";

    /// <summary>The program sent more commands than a program may.</summary>
    public const string TooManyCalls = "tooManyCalls";

    /// <summary>The program is not valid JavaScript.</summary>
    public const string Syntax = "syntax";

    /// <summary>The program threw and did not catch it, or a command failed uncaught without a type of its own.</summary>
    public const string Runtime = "runtime";

    /// <summary>The program called a command the global object does not have.</summary>
    public const string NoSuchCommand = "noSuchCommand";

    /// <summary>The session may not call the tool the command stands for: the fleet's one type of a refusal by the grant.</summary>
    /// <remarks>The same word as the gate's own refusal of a tool, so a client branches on one type whether a tool or a program's command was refused.</remarks>
    public const string NotGranted = GrantOptions.NotGranted;

    /// <summary>The call was malformed: no code, a timeout out of range, a command given something other than one object.</summary>
    public const string Invalid = "invalid";
}
