namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>Which tools this session may use, read from the address it was opened at.</summary>
/// <remarks>
/// <para>
/// The platform gives a role a list of tools and appends it to the address, once per tool:
/// <c>…/mcp?root=/workspaces/task&amp;tool=read_file&amp;tool=search_text</c>. The model cannot reach the address, so the
/// server is the one place the list cannot be talked around. A session that names no tool may use them all, which is
/// what a server started as a program for one caller has always been.
/// </para>
/// <para>
/// Nine servers of the fleet kept a copy of this class, and a role granted <c>run_tools</c> reached every tool of three of
/// them — the copies held at the door and nowhere else. The grant is checked at the door by
/// <see cref="SessionRegistration.WithSessionGrant"/> and for every forwarded step by <see cref="ToolForwarding"/>,
/// both through <see cref="IToolGate"/>.
/// </para>
/// </remarks>
public sealed class SessionGrant(ISessionAddress address)
{
    /// <summary>What the address calls a tool it grants; repeated for each one.</summary>
    public const string Parameter = "tool";

    /// <summary>The tools this session named, trimmed and each once; empty when it named none.</summary>
    public IReadOnlyList<string> All =>
    [
        .. address.Values(Parameter)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>Whether the address was there to name tools at all, which a program over stdio never has.</summary>
    internal bool IsAddressed => address.IsPresent;

    /// <summary>Whether this session may call the tool: every tool when it named none.</summary>
    public bool Allows(string tool)
    {
        var granted = All;

        return granted.Count == 0 || granted.Contains(tool, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The refusal, in the words a caller needs to stop asking.</summary>
    /// <remarks>
    /// The fleet's words, kept so that a refusal reads the same from every server a role reaches; "this request" instead of
    /// "this session" where the address says each request stands alone (<see cref="ISessionAddress.Subject"/>).
    /// </remarks>
    public string Refusal(string tool)
    {
        var granted = All;
        var subject = address.Subject;

        return granted.Count == 0
            ? $"'{tool}' was not granted to this {subject}, which was granted no tool: name each tool it may use in the address, ?{Parameter}=<name>"
            : $"'{tool}' was not granted to this {subject}, which may use {string.Join(", ", granted)}";
    }
}
