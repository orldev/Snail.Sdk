using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>A session's address fixed for a test: <c>new FixedAddress(("tool", "read_file"), ("root", "/w/task"))</c>.</summary>
/// <remarks>
/// What <see cref="SessionGrant"/> and <see cref="SessionRoots"/> read without a server over HTTP in front of them, so a
/// test states the address a platform would have opened the session at. Its values are given as they are, blanks included.
/// </remarks>
/// <param name="values">Each parameter with one value; a parameter given twice has both.</param>
public sealed class FixedAddress(params (string Parameter, string Value)[] values) : ISessionAddress
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Values(string parameter) =>
        [.. values.Where(value => string.Equals(value.Parameter, parameter, StringComparison.Ordinal)).Select(value => value.Value)];
}
