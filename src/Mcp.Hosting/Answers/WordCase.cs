namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>How an answer spells an enum value of several words; a server chooses one and keeps it in every answer.</summary>
public enum WordCase
{
    /// <summary>Every letter lower case, the words run together: <c>InProgress</c> is <c>inprogress</c>.</summary>
    Lower,

    /// <summary>Camel case, the way JSON names a property: <c>InProgress</c> is <c>inProgress</c>.</summary>
    Camel,
}
