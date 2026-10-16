using System.Collections.ObjectModel;
using System.Reflection;

namespace Snail.Sdk.Docs;

/// <summary>The nullable annotations the compiler wrote for one use of a type, read in the order the type is written.</summary>
/// <remarks>
/// A MetadataLoadContext has no NullabilityInfoContext, so the attributes are read as the compiler documents them
/// (roslyn docs/features/nullable-metadata.md): NullableAttribute on the use holds a byte per position of the type in
/// pre-order, or one byte for all of them; without it, NullableContextAttribute on the nearest method or type holds the
/// default. 2 is annotated (string?), 1 is not, 0 is oblivious. A non-generic value type takes no position, and
/// a nullable value type only those of its argument.
/// </remarks>
internal sealed class Annotations
{
    private const string Nullable = "System.Runtime.CompilerServices.NullableAttribute";

    private const string NullableContext = "System.Runtime.CompilerServices.NullableContextAttribute";

    private const byte Annotated = 2;

    private readonly IReadOnlyList<byte> _flags;

    private int _next;

    private Annotations(IReadOnlyList<byte> flags) => _flags = flags;

    /// <summary>The annotations of a use — a parameter, a return, a property, a field — in the context of the member that holds it.</summary>
    public static Annotations Of(IEnumerable<CustomAttributeData> use, MemberInfo? context)
    {
        var own = Flags(use, Nullable);

        if (own is not null)
            return new Annotations(own);

        for (var member = context; member is not null; member = member.DeclaringType)
        {
            if (Flags(member.CustomAttributes, NullableContext) is { } inherited)
                return new Annotations(inherited);
        }

        return new Annotations([]);
    }

    /// <summary>Whether the next position of the type is annotated; a single byte answers for every position.</summary>
    public bool IsNextAnnotated()
    {
        var flag = _flags.Count switch
        {
            0 => (byte)0,
            1 => _flags[0],
            _ => _next < _flags.Count ? _flags[_next] : (byte)0
        };

        _next++;

        return flag == Annotated;
    }

    private static IReadOnlyList<byte>? Flags(IEnumerable<CustomAttributeData> attributes, string attributeName)
    {
        var attribute = attributes.FirstOrDefault(candidate => candidate.AttributeType.FullName == attributeName);

        if (attribute is not { ConstructorArguments: [var argument] })
            return null;

        return argument.Value switch
        {
            byte single => [single],
            ReadOnlyCollection<CustomAttributeTypedArgument> many => [.. many.Select(element => (byte)element.Value!)],
            _ => null
        };
    }
}
