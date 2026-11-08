using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>A setting the configuration binder could not convert to its property's type, said as a <see cref="StartRefusedException"/> that names the variable and what it holds.</summary>
/// <remarks>
/// <para>
/// <c>SNAIL_MCP_BLENDER_BRIDGE__PORT=abc</c> stopped Snail.MCP.Blender with an unhandled
/// <see cref="InvalidOperationException"/>, "Failed to convert configuration value 'abc' at 'Bridge:Port' to type
/// 'System.Int32'.", a trace and exit code 134 — a crash, where a person had set a word for a number (0.5.0-local.6).
/// The binder says it the same way wherever it runs: while a server binds its settings before any host exists
/// (<see cref="ServerSettings.Bind(string, IConfiguration, Action{IConfiguration})"/>), and in the options a host
/// validates or a hosted service reads as it starts (<see cref="StartRefusal"/>).
/// </para>
/// <para>
/// The binder throws nothing of its own type, so the failure is known by its shape, never by its kind alone, which a
/// server's own code throws too: an <see cref="InvalidOperationException"/> caused by a failed parse — a
/// <see cref="FormatException"/>, <see cref="ArgumentException"/>, <see cref="OverflowException"/> or
/// <see cref="NotSupportedException"/> — whose message is one the binder writes, whole. Three are written: the reflection
/// binder's and the binding source generator's of .NET 10 alike, "Failed to convert configuration value '{value}' at
/// '{path}' to type '{type}'." (the generator turns on for every native server, <c>EnableConfigurationBindingGenerator</c>);
/// the generator's of .NET 8, "Failed to convert configuration value at '{path}' to type '{type}'.", which leaves the value
/// out, so it is looked up in the settings at the path; and the reflection binder's under <c>UseSystemResourceKeys</c>,
/// its resource key and arguments, "Error_FailedBinding, {value}, {path}, {type}". Any other message is not a refusal.
/// </para>
/// </remarks>
internal static partial class BindingRefusal
{
    private const string KeyOfMessage = "Error_FailedBinding, ";

    private const string Separator = ", ";

    private const string NullableOf = "System.Nullable`1[";

    /// <summary>Whether a failure is the binder's of a value its property's type cannot hold.</summary>
    public static bool Is(Exception? failure) => Read(failure) is not null;

    /// <summary>The refusal a failure of the binder was, naming the setting by <paramref name="server"/>'s variable; null when it was a failure of another kind.</summary>
    /// <param name="failure">What was thrown.</param>
    /// <param name="server">The server's short name, or null when it is not known, and the key alone is named.</param>
    /// <param name="settings">The settings that were bound, where a value the message leaves out is looked up; null for none.</param>
    public static StartRefusedException? Of(Exception failure, string? server, IConfiguration? settings)
    {
        if (Read(failure) is not { } unconverted)
            return null;

        var setting = server is null ? unconverted.Path : ServerSettings.NameOfSetting(server, unconverted.Path);
        var predicate = PredicateOf(unconverted.Type, failure);
        var value = unconverted.Value ?? settings?[unconverted.Path];

        var sentence = value is null
            ? $"{setting} {predicate}."
            : $"{setting} is '{OneLine(value)}'; it {predicate}.";

        return new StartRefusedException(sentence, failure);
    }

    /// <summary>The value, the path and the type a failure of the binder names; null for a failure of another shape.</summary>
    private static Unconverted? Read(Exception? failure)
    {
        if (failure is not InvalidOperationException { InnerException: FormatException or ArgumentException or OverflowException or NotSupportedException })
            return null;

        var message = failure.Message;
        var reflected = Reflected().Match(message);

        if (reflected.Success)
            return new Unconverted(reflected.Groups["value"].Value, reflected.Groups["path"].Value, reflected.Groups["type"].Value);

        var generated = GeneratedWithoutValue().Match(message);

        if (generated.Success)
            return new Unconverted(null, generated.Groups["path"].Value, generated.Groups["type"].Value);

        return message.StartsWith(KeyOfMessage, StringComparison.Ordinal) ? FromResourceKey(message[KeyOfMessage.Length..]) : null;
    }

    /// <summary>The arguments of the message under <c>UseSystemResourceKeys</c>, read from the right, since only the value may hold a comma.</summary>
    private static Unconverted? FromResourceKey(string arguments)
    {
        var beforeType = arguments.LastIndexOf(Separator, StringComparison.Ordinal);

        if (beforeType <= 0)
            return null;

        var beforePath = arguments.LastIndexOf(Separator, beforeType - 1, StringComparison.Ordinal);

        if (beforePath < 0)
            return null;

        var path = arguments[(beforePath + Separator.Length)..beforeType];
        var type = arguments[(beforeType + Separator.Length)..];

        return path.Length > 0 && type.Length > 0 ? new Unconverted(arguments[..beforePath], path, type) : null;
    }

    /// <summary>What the value is not, in words a person setting a variable reads: <c>is not a number (Int32)</c>.</summary>
    private static string PredicateOf(string type, Exception failure)
    {
        var underlying = UnderlyingOf(type);
        var name = underlying[(underlying.LastIndexOfAny(['.', '+']) + 1)..];

        if (InnermostOf(failure) is OverflowException)
            return $"is out of the range of {name}";

        var expected = underlying switch
        {
            "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64"
                or "System.Int128" or "System.UInt128" or "System.Half" or "System.Single" or "System.Double" or "System.Decimal" => "a number",
            "System.Boolean" => "true or false",
            "System.TimeSpan" => "a time span such as 00:05:00",
            "System.DateTime" or "System.DateTimeOffset" or "System.DateOnly" => "a date",
            "System.TimeOnly" => "a time of day",
            "System.Guid" => "a GUID",
            "System.Uri" => "a URI",
            "System.Char" => "a single character",
            "System.Version" => "a version such as 1.2.3",
            _ => null
        };

        return expected is null ? $"is not a valid {name}" : $"is not {expected} ({name})";
    }

    /// <summary>The type a nullable one wraps, which is what the value had to be.</summary>
    private static string UnderlyingOf(string type) =>
        type.StartsWith(NullableOf, StringComparison.Ordinal) && type.EndsWith(']') ? type[NullableOf.Length..^1] : type;

    private static Exception InnermostOf(Exception failure)
    {
        var innermost = failure;

        while (innermost.InnerException is { } inner)
            innermost = inner;

        return innermost;
    }

    private static string OneLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    [GeneratedRegex(@"\AFailed to convert configuration value '(?<value>.*)' at '(?<path>[^']+)' to type '(?<type>[^']+)'\.\z", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Reflected();

    [GeneratedRegex(@"\AFailed to convert configuration value at '(?<path>[^']+)' to type '(?<type>[^']+)'\.\z", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedWithoutValue();

    /// <summary>What the binder could not convert: the value, when its message says it, the path of the setting and the type.</summary>
    private sealed record Unconverted(string? Value, string Path, string Type);
}
