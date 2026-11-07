using System.Text.Json.Nodes;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>What a program did: what it returned, what it wrote to its log, the commands it sent, and why it stopped when it did not finish.</summary>
/// <remarks>
/// The commands are part of the answer even when the program failed: a program that committed and then stopped has
/// changed the repository, and the caller has to see that in the reply rather than by going and looking (Snail.MCP.Git).
/// </remarks>
/// <param name="Result">What the program returned, as JSON; null when it returned nothing or did not finish.</param>
/// <param name="Log">The lines it wrote with <c>log(…)</c>.</param>
/// <param name="Calls">Every command it sent, refused ones included, in order.</param>
/// <param name="Failure">Why it stopped; null when it finished.</param>
public sealed record ProgramRun(JsonNode? Result, IReadOnlyList<string> Log, IReadOnlyList<ProgramCall> Calls, ProgramFailure? Failure);

/// <summary>One command a program sent, as its trace reports it.</summary>
/// <param name="Command">The command's name.</param>
/// <param name="IsOk">Whether it went through.</param>
/// <param name="Milliseconds">How long it took.</param>
public sealed record ProgramCall(string Command, bool IsOk, int Milliseconds);

/// <summary>Why a program stopped, in words the model can act on.</summary>
/// <param name="Type">The kind of stop, one of <see cref="ProgramFailures"/> or the type of the command that failed uncaught.</param>
/// <param name="Error">What happened.</param>
/// <param name="Hint">What to do about it.</param>
/// <param name="Details">What the failed command reported beside its error.</param>
public sealed record ProgramFailure(string Type, string Error, string? Hint = null, JsonNode? Details = null);
