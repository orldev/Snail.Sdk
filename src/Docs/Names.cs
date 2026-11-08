using System.Reflection;
using System.Text;

namespace Snail.Sdk.Docs;

/// <summary>How a type or member is written: as C# shows it to a reader, and as an XML documentation id.</summary>
internal static class Names
{
    private static readonly Dictionary<string, string> Keywords = new(StringComparer.Ordinal)
    {
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Decimal"] = "decimal",
        ["System.Double"] = "double",
        ["System.Single"] = "float",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Object"] = "object",
        ["System.String"] = "string",
        ["System.Void"] = "void"
    };

    /// <summary>The type as C# writes it: keywords, generic arguments in angle brackets, nullable value types with a question mark.</summary>
    public static string Of(Type type) => Of(type, annotations: null);

    /// <summary>The type as C# writes it, with the question marks its annotations put on reference types and type parameters.</summary>
    public static string Of(Type type, Annotations? annotations)
    {
        if (type.IsByRef)
            return Of(type.GetElementType()!, annotations);

        if (type.IsArray)
        {
            var array = Mark(annotations);

            return $"{Of(type.GetElementType()!, annotations)}[{new string(',', type.GetArrayRank() - 1)}]{array}";
        }

        if (type.IsGenericParameter)
            return $"{type.Name}{Mark(annotations)}";

        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
            return $"{Of(type.GetGenericArguments()[0], annotations)}?";

        var mark = type.IsValueType ? Skip(type, annotations) : Mark(annotations);
        var name = type.FullName is { } full && Keywords.TryGetValue(full, out var keyword) ? keyword : Bare(type);

        if (!type.IsGenericType)
            return $"{name}{mark}";

        var arguments = type.GetGenericArguments().Select(argument => Of(argument, annotations)).ToList();

        return $"{name}<{string.Join(", ", arguments)}>{mark}";
    }

    /// <summary>A question mark when the reference at this position is annotated.</summary>
    private static string Mark(Annotations? annotations) => annotations?.IsNextAnnotated() == true ? "?" : string.Empty;

    /// <summary>A generic value type takes a position it never marks; a plain one takes none.</summary>
    private static string Skip(Type type, Annotations? annotations)
    {
        if (type.IsGenericType)
            annotations?.IsNextAnnotated();

        return string.Empty;
    }

    /// <summary>A member's signature as a reader looks for it.</summary>
    public static string Signature(MemberInfo member) => member switch
    {
        ConstructorInfo constructor => $"{Bare(constructor.DeclaringType!)}({Parameters(constructor)})",
        MethodInfo method => $"{Of(method.ReturnType, Annotations.Of(method.ReturnParameter.CustomAttributes, method))} {Receiver(method)}{method.Name}{TypeParameters(method)}({Parameters(method)})",
        PropertyInfo property => $"{Of(property.PropertyType, Annotations.Of(property.CustomAttributes, property))} {property.Name} {{ {Accessors(property)} }}",
        FieldInfo field => $"{(field.IsLiteral ? "const " : field.IsStatic ? "static " : string.Empty)}{Of(field.FieldType, Annotations.Of(field.CustomAttributes, field))} {field.Name}",
        EventInfo @event => $"event {Of(@event.EventHandlerType!)} {@event.Name}",
        _ => member.Name
    };

    /// <summary>The XML documentation id the compiler writes for a type.</summary>
    public static string DocumentationId(Type type) => $"T:{DocumentationName(type)}";

    /// <summary>The XML documentation id the compiler writes for a member.</summary>
    public static string DocumentationId(MemberInfo member) => DocumentationId(member, member.DeclaringType!);

    /// <summary>The id the same member would have if another type — a base, an interface — declared it.</summary>
    public static string DocumentationId(MemberInfo member, Type declaringType)
    {
        var owner = DocumentationName(declaringType.IsConstructedGenericType ? declaringType.GetGenericTypeDefinition() : declaringType);

        return member switch
        {
            ConstructorInfo constructor => $"M:{owner}.{(constructor.IsStatic ? "#cctor" : "#ctor")}{DocumentationParameters(constructor)}",
            MethodInfo method => $"M:{owner}.{method.Name}{(method.IsGenericMethod ? $"``{method.GetGenericArguments().Length}" : string.Empty)}{DocumentationParameters(method)}",
            PropertyInfo property => $"P:{owner}.{property.Name}{IndexerParameters(property)}",
            FieldInfo field => $"F:{owner}.{field.Name}",
            EventInfo @event => $"E:{owner}.{@event.Name}",
            _ => member.Name
        };
    }

    private static string Bare(Type type) => type.Name.IndexOf('`', StringComparison.Ordinal) is var tick and > 0 ? type.Name[..tick] : type.Name;

    private static string TypeParameters(MethodInfo method) =>
        method.IsGenericMethodDefinition ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>" : string.Empty;

    private static string Parameters(MethodBase method)
    {
        var written = method.GetParameters().Select(parameter => $"{Modifier(parameter)}{Of(parameter.ParameterType, Annotations.Of(parameter.CustomAttributes, method))} {parameter.Name}{Default(parameter)}");
        var extended = IsExtension(method) ? written.Select((parameter, index) => index == 0 ? $"this {parameter}" : parameter) : written;

        return string.Join(", ", extended);
    }

    /// <summary>An optional parameter's default as C# writes it.</summary>
    private static string Default(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue)
            return string.Empty;

        var value = parameter.RawDefaultValue switch
        {
            null => parameter.ParameterType.IsValueType && !parameter.ParameterType.IsGenericType ? "default" : "null",
            string text => $"\"{text}\"",
            bool flag => flag ? "true" : "false",
            char letter => $"'{letter}'",
            IFormattable number => number.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            var other => other.ToString()
        };

        return $" = {value}";
    }

    /// <summary>The type a static extension member extends, as a prefix — <c>JobOwners.</c> for <c>JobOwners.Session</c> — or nothing for any other method.</summary>
    private static string Receiver(MethodInfo method) => Extended(method) is { } extended ? $"{Bare(extended)}." : string.Empty;

    /// <summary>
    /// The type a static extension member extends — <c>JobOwners</c> for <c>JobOwners.Session</c>, declared in
    /// <c>extension(JobOwners)</c> of another class — or null for any other method.
    /// </summary>
    /// <remarks>
    /// The compiler writes a static extension member as a plain static method of the class that declares the block, with
    /// nothing on it that names the type it extends: the row said <c>Session(IServiceProvider)</c> under that class, and a
    /// reader looking for <c>JobOwners.Session</c> did not find it. The type is read from the block's grouping type, whose
    /// marker method takes the extended type as its one parameter.
    /// </remarks>
    public static Type? Extended(MethodInfo method)
    {
        if (!method.IsStatic || IsExtension(method) || method.DeclaringType is not { IsAbstract: true, IsSealed: true } owner)
            return null;

        var parameters = ParameterTypes(method);

        foreach (var group in owner.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Where(nested => nested.Name.StartsWith("<G>$", StringComparison.Ordinal)))
        {
            var skeleton = group.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate => candidate.Name == method.Name && ParameterTypes(candidate).SequenceEqual(parameters, StringComparer.Ordinal));

            if (skeleton is not null && ExtendedBy(group, skeleton) is { } extended)
                return extended;
        }

        return null;
    }

    /// <summary>The type a member of a grouping type extends: the parameter of the marker its attribute names, or of the group's only marker.</summary>
    private static Type? ExtendedBy(Type group, MethodInfo skeleton)
    {
        var markerName = skeleton.CustomAttributes
            .FirstOrDefault(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionMarkerAttribute")?
            .ConstructorArguments.FirstOrDefault().Value as string;

        var markers = group.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .Where(nested => markerName is null || nested.Name == markerName)
            .Select(nested => nested.GetMethod("<Extension>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(marker => marker is { } found && found.GetParameters().Length == 1)
            .ToList();

        return markers.Count == 1 ? markers[0]!.GetParameters()[0].ParameterType : null;
    }

    private static IEnumerable<string> ParameterTypes(MethodInfo method) =>
        method.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name);

    private static bool IsExtension(MethodBase method) =>
        method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute");

    private static string Modifier(ParameterInfo parameter) => parameter switch
    {
        { IsOut: true } => "out ",
        { ParameterType.IsByRef: true, IsIn: true } => "in ",
        { ParameterType.IsByRef: true } => "ref ",
        _ => string.Empty
    };

    private static string Accessors(PropertyInfo property)
    {
        var accessors = new List<string>();

        if (property.GetMethod is { IsPublic: true } or { IsFamily: true })
            accessors.Add("get;");

        if (property.SetMethod is { IsPublic: true } or { IsFamily: true })
            accessors.Add(IsInitOnly(property.SetMethod) ? "init;" : "set;");

        return string.Join(' ', accessors);
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string DocumentationName(Type type)
    {
        if (type.IsGenericParameter)
            return type.DeclaringMethod is null ? $"`{type.GenericParameterPosition}" : $"``{type.GenericParameterPosition}";

        var name = new StringBuilder(type.IsNested ? $"{DocumentationName(type.DeclaringType!)}.{type.Name}" : type.FullName ?? type.Name);

        return name.Replace('+', '.').ToString();
    }

    private static string DocumentationParameters(MethodBase method)
    {
        var parameters = method.GetParameters();

        return parameters.Length == 0 ? string.Empty : $"({string.Join(',', parameters.Select(parameter => DocumentationType(parameter.ParameterType)))})";
    }

    private static string IndexerParameters(PropertyInfo property)
    {
        var parameters = property.GetIndexParameters();

        return parameters.Length == 0 ? string.Empty : $"({string.Join(',', parameters.Select(parameter => DocumentationType(parameter.ParameterType)))})";
    }

    private static string DocumentationType(Type type)
    {
        if (type.IsByRef)
            return $"{DocumentationType(type.GetElementType()!)}@";

        if (type.IsArray)
            return $"{DocumentationType(type.GetElementType()!)}[]";

        if (type.IsGenericParameter)
            return DocumentationName(type);

        if (!type.IsGenericType)
            return DocumentationName(type);

        var definition = type.GetGenericTypeDefinition();
        var bare = DocumentationName(definition);
        var arguments = string.Join(',', type.GetGenericArguments().Select(DocumentationType));

        return $"{bare[..bare.IndexOf('`', StringComparison.Ordinal)]}{{{arguments}}}";
    }
}
