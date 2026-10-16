using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Snail.Sdk.Analyzers;

/// <summary>The names a library has already released, read from its PublicAPI.Shipped.txt.</summary>
/// <remarks>
/// A shipped name cannot change without breaking the library's users, so a naming rule says nothing about it: the rules
/// shape what is written next, and a rename of a released member is a decision for a major version, not for a build.
/// Each line of the file is kept by its qualified name, without type arguments, and with what it declares: a type, a
/// method, or a value (a property, a field, an event, an enum member); every dotted prefix of the name is kept as a type
/// or namespace. A symbol matches a line only as a member of the type the line names and only of the kind the line
/// declares, so a local, a parameter or a local function that shares a released member's name is not taken for it.
/// </remarks>
internal sealed class ShippedApi
{
    private static readonly ShippedApi None = new(ImmutableDictionary<string, Declared>.Empty);

    private static readonly string[] Accessors = [".get", ".set", ".init", ".add", ".remove"];

    private static readonly string[] Modifiers = ["static ", "abstract ", "virtual ", "override ", "sealed ", "const ", "readonly ", "new ", "required ", "*REMOVED*"];

    private static readonly SymbolDisplayFormat QualifiedName = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None);

    private readonly ImmutableDictionary<string, Declared> _names;

    private ShippedApi(ImmutableDictionary<string, Declared> names) => _names = names;

    /// <summary>What a shipped line declares under its name.</summary>
    [Flags]
    private enum Declared
    {
        Nothing = 0,
        Container = 1,
        Method = 2,
        Value = 4
    }

    /// <summary>The shipped names of the compilation, or none when it keeps no PublicAPI.Shipped.txt.</summary>
    public static ShippedApi Of(AnalyzerOptions options, CancellationToken cancellationToken)
    {
        var files = options.AdditionalFiles.Where(file => Path.GetFileName(file.Path).Equals("PublicAPI.Shipped.txt", StringComparison.OrdinalIgnoreCase));
        var names = new Dictionary<string, Declared>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (file.GetText(cancellationToken) is not { } text)
                continue;

            foreach (var line in text.Lines)
                Add(names, WithoutModifiers(WithoutTypeArguments(line.ToString().Trim())));
        }

        return names.Count == 0 ? None : new ShippedApi(names.ToImmutableDictionary(StringComparer.Ordinal));
    }

    /// <summary>Whether the type or member was released under this name.</summary>
    public bool Contains(ISymbol symbol)
    {
        if (_names.Count == 0 || symbol.ContainingSymbol is not (INamespaceSymbol or INamedTypeSymbol))
            return false;

        var kind = symbol switch
        {
            INamedTypeSymbol => Declared.Container,
            IMethodSymbol => Declared.Method,
            IPropertySymbol or IFieldSymbol or IEventSymbol => Declared.Value,
            _ => Declared.Nothing
        };

        var name = symbol is INamedTypeSymbol type ? type.ToDisplayString(QualifiedName) : $"{symbol.ContainingType?.ToDisplayString(QualifiedName)}.{symbol.Name}";

        return kind != Declared.Nothing && _names.TryGetValue(name, out var shipped) && (shipped & kind) != 0;
    }

    /// <summary>The line without what precedes the name: static, override, const and the rest, ~ for an oblivious line.</summary>
    private static string WithoutModifiers(string line)
    {
        var rest = line.TrimStart('~');

        while (Modifiers.FirstOrDefault(modifier => rest.StartsWith(modifier, StringComparison.Ordinal)) is { } modifier)
            rest = rest.Substring(modifier.Length).TrimStart('~');

        return rest;
    }

    /// <summary>The line with every type argument list taken out: Outer&lt;T&gt;.Find&lt;TKey, TValue&gt;(…) reads Outer.Find(…).</summary>
    private static string WithoutTypeArguments(string line)
    {
        var bare = new StringBuilder(line.Length);
        var depth = 0;

        foreach (var character in line)
        {
            if (character == '<')
                depth++;
            else if (character == '>' && depth > 0)
                depth--;
            else if (depth == 0)
                bare.Append(character);
        }

        return bare.ToString();
    }

    /// <summary>
    /// Keeps a line's name with what it declares: a line that is only a name is a type, a name followed by its parameters
    /// a method, a name followed by its type (<c>-&gt;</c> or <c>=</c>) a value — an accessor line gives its property's.
    /// </summary>
    private static void Add(Dictionary<string, Declared> names, string bare)
    {
        if (bare.Length == 0 || bare.StartsWith("#", StringComparison.Ordinal))
            return;

        var end = bare.IndexOfAny(['(', ' ']);
        var head = end < 0 ? bare : bare.Substring(0, end);
        var kind = end < 0 ? Declared.Container : bare[end] == '(' ? Declared.Method : Declared.Value;

        if (kind == Declared.Value && Accessors.FirstOrDefault(accessor => head.EndsWith(accessor, StringComparison.Ordinal)) is { } accessor)
            head = head.Substring(0, head.Length - accessor.Length);

        for (var dot = head.IndexOf('.'); dot >= 0; dot = head.IndexOf('.', dot + 1))
            Mark(names, head.Substring(0, dot), Declared.Container);

        Mark(names, head, kind);
    }

    private static void Mark(Dictionary<string, Declared> names, string name, Declared kind) =>
        names[name] = names.TryGetValue(name, out var known) ? known | kind : kind;
}
