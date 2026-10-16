using System.Text.Json;
using Snail.Sdk.Docs;

const string usage = """
    Snail.Sdk.Docs api <assembly> <xml documentation> <file listing the references> <output folder>
    Snail.Sdk.Docs tools <server assembly> <docs/tools.json, or a path that does not exist> <output folder>
    Snail.Sdk.Docs site <file of the site's facts> <output folder>
    """;

string? written;

try
{
    written = args switch
    {
        ["api", var assembly, var documentation, var references, var output] => ApiReference.Write(assembly, documentation, File.ReadAllLines(references), output),
        ["tools", var server, var settings, var output] => ToolReference.Write(server, settings, output),
        ["site", var facts, var output] => Site.Write(SiteFacts.Read(facts), output),
        _ => null
    };
}
catch (DocsError error)
{
    Console.Error.WriteLine(DocsError.Line(error.Code, error.Message));

    return 1;
}
catch (Exception unreadable) when (unreadable is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(DocsError.Line(DocsError.Unreadable, unreadable.Message));

    return 1;
}

if (written is null)
{
    Console.Error.WriteLine($"Usage:\n{usage}");

    return 2;
}

Console.WriteLine($"Snail.Sdk.Docs: {written}");

return 0;
