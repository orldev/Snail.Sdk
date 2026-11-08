using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>What a failed start says when the settings were at fault: options that failed their validation, or a value the binder could not convert, said as a <see cref="StartRefusedException"/> in one line.</summary>
/// <remarks>
/// <para>
/// A server whose options are validated at the start — <c>ValidateOnStart()</c>, or a hosted service that reads
/// <c>IOptions&lt;T&gt;.Value</c> as it starts — stopped with an <see cref="OptionsValidationException"/> out of
/// <see cref="IHost.StartAsync"/>: unhandled, a crash with a trace and exit code 134, where a person had only set a wrong
/// value (Snail.MCP.BuildBridge, Blender, AfterEffects). The program of <c>dotnet new s3l-mcp</c> catches a
/// <see cref="StartRefusedException"/> and says it in one line with exit code 1, so the failures are carried as one.
/// </para>
/// <para>
/// Options bound from the settings fail the same start earlier, before any validation, when a value cannot be converted
/// to its property's type — <c>SNAIL_MCP_BLENDER_BRIDGE__PORT=abc</c> — with the binder's <see cref="InvalidOperationException"/>,
/// which is said as the setting's variable and what it holds (<see cref="BindingRefusal"/>).
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
    /// <param name="failure">What the start threw.</param>
    /// <param name="server">The server's short name, which names a setting the binder could not convert by its variable; null names the key alone.</param>
    /// <param name="settings">The host's configuration, where a value the binder's message leaves out is looked up; null for none.</param>
    /// <remarks>
    /// An <see cref="AggregateException"/> — several options failing, or several hosted services — is a refusal when every
    /// failure in it is one, and says them all, one after another.
    /// </remarks>
    public static StartRefusedException? Of(Exception failure, string? server, IConfiguration? settings)
    {
        if (failure is OptionsValidationException invalid)
            return new StartRefusedException(OneLine(invalid), invalid);

        if (BindingRefusal.Of(failure, server, settings) is { } unconverted)
            return unconverted;

        if (failure is not AggregateException several || !IsRefusal(several))
            return null;

        var said = several.Flatten().InnerExceptions.Select(inner => SaidOf(inner, server, settings));

        return new StartRefusedException(Joined(said), several);
    }

    /// <summary>Whether a failure is a refusal for what the operator set: an options validation, a value the binder could not convert, a <see cref="StartRefusedException"/>, or several of them.</summary>
    public static bool IsRefusal(Exception? failure) => failure switch
    {
        OptionsValidationException or StartRefusedException => true,
        AggregateException several => several.Flatten().InnerExceptions.All(IsRefusal),
        _ => BindingRefusal.Is(failure)
    };

    /// <summary>Failures said one after another in one line: a sentence followed by a space, a bare fragment by a semicolon.</summary>
    /// <remarks>
    /// Snail.MCP.BuildBridge writes a sentence per setting, ending with a full stop, and joined by semicolons alone they read
    /// "… above zero.; SNAIL_MCP_BUILDBRIDGE_…"; a fragment without its stop still needs the semicolon to end it.
    /// </remarks>
    internal static string Joined(IEnumerable<string> failures)
    {
        var line = new StringBuilder();

        foreach (var failure in failures)
        {
            if (line.Length > 0)
                line.Append(IsSentence(line) ? " " : "; ");

            line.Append(failure);
        }

        return line.ToString();
    }

    private static bool IsSentence(StringBuilder line) => line[^1] is '.' or '!' or '?';

    private static string SaidOf(Exception failure, string? server, IConfiguration? settings) => failure switch
    {
        OptionsValidationException options => OneLine(options),
        StartRefusedException refused => refused.Message,
        _ => BindingRefusal.Of(failure, server, settings)?.Message ?? failure.Message
    };

    /// <summary>The failures of an options validation as one line: each failure with its line breaks made spaces, joined as <see cref="Joined"/> joins them.</summary>
    private static string OneLine(OptionsValidationException invalid)
    {
        IEnumerable<string> failures = invalid.Failures.Any() ? invalid.Failures : [invalid.Message];
        var lines = failures.Select(failure => string.Join(' ', failure.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

        return Joined(lines);
    }
}
