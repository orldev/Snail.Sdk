using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>What a failed start says when the settings were at fault: options that failed their validation, said as a <see cref="StartRefusedException"/> in one line.</summary>
/// <remarks>
/// <para>
/// A server whose options are validated at the start — <c>ValidateOnStart()</c>, or a hosted service that reads
/// <c>IOptions&lt;T&gt;.Value</c> as it starts — stopped with an <see cref="OptionsValidationException"/> out of
/// <see cref="IHost.StartAsync"/>: unhandled, a crash with a trace and exit code 134, where a person had only set a wrong
/// value (Snail.MCP.BuildBridge, Blender, AfterEffects; 0.5.0-local.5). The program of <c>dotnet new s3l-mcp</c> catches a
/// <see cref="StartRefusedException"/> and says it in one line with exit code 1, so the failures are carried as one.
/// </para>
/// <para>
/// The generic host logs a failed start as an error with its trace before it throws; for a refusal that line is left
/// out (<see cref="StderrLogging"/>), since the refusal itself is what the program says.
/// </para>
/// </remarks>
internal static class StartRefusal
{
    /// <summary>The category the generic host logs a failed start in.</summary>
    internal const string HostCategory = "Microsoft.Extensions.Hosting.Internal.Host";

    /// <summary>The refusal a failed start was, in one line; null when it was a failure of another kind.</summary>
    /// <remarks>
    /// An <see cref="AggregateException"/> — several options failing, or several hosted services — is a refusal when every
    /// failure in it is one, and says them all, one after another.
    /// </remarks>
    public static StartRefusedException? Of(Exception failure)
    {
        if (failure is OptionsValidationException invalid)
            return new StartRefusedException(OneLine(invalid), invalid);

        if (failure is not AggregateException several || !IsRefusal(several))
            return null;

        var said = several.Flatten().InnerExceptions.Select(inner => inner is OptionsValidationException options ? OneLine(options) : inner.Message);

        return new StartRefusedException(string.Join("; ", said), several);
    }

    /// <summary>Whether a failure is a refusal for what the operator set: an options validation, a <see cref="StartRefusedException"/>, or several of them.</summary>
    public static bool IsRefusal(Exception? failure) => failure switch
    {
        OptionsValidationException or StartRefusedException => true,
        AggregateException several => several.Flatten().InnerExceptions.All(inner => inner is OptionsValidationException or StartRefusedException),
        _ => false
    };

    /// <summary>The failures of an options validation as one line: each failure, line breaks and all, joined by semicolons.</summary>
    private static string OneLine(OptionsValidationException invalid)
    {
        IEnumerable<string> failures = invalid.Failures.Any() ? invalid.Failures : [invalid.Message];
        var lines = failures.Select(failure => string.Join(' ', failure.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

        return string.Join("; ", lines);
    }
}
