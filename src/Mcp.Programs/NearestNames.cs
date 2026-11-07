namespace Snail.Sdk.Mcp.Programs;

/// <summary>The commands whose names come closest to one a program asked for and the catalog does not hold.</summary>
/// <remarks>
/// Two ways to miss, both met in the fleet: the words of a tool turned round — Snail.MCP.AfterEffects' tools read
/// <c>ae_remove_layers</c> where its commands read <c>layer_remove</c>, six of eighteen failed calls in a fortnight of logs —
/// and a slip of the keys, <c>stauts</c> for <c>status</c>. So a name is near when it shares a word with the one asked for
/// (a plural counts as its singular) or lies within a few edits of it.
/// </remarks>
internal static class NearestNames
{
    /// <summary>At most <paramref name="most"/> names nearest to <paramref name="asked"/>: more shared words first, then fewer edits.</summary>
    public static IReadOnlyList<string> Of(string asked, IReadOnlyList<string> names, int most = 3)
    {
        var wanted = Words(asked);
        var allowed = Math.Max(2, asked.Length / 3);
        var scored = new List<(string Name, int Shared, int Edits)>();

        foreach (var name in names)
        {
            var shared = Words(name).Count(word => wanted.Contains(word, StringComparer.OrdinalIgnoreCase));
            var edits = Edits(asked.ToLowerInvariant(), name.ToLowerInvariant());

            if (shared > 0 || edits <= allowed)
                scored.Add((name, shared, edits));
        }

        var nearest = scored
            .OrderByDescending(candidate => candidate.Shared)
            .ThenBy(candidate => candidate.Edits)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Take(most);

        return [.. nearest.Select(candidate => candidate.Name)];
    }

    private static string[] Words(string name) =>
        [.. name.Split(['_', '-', '.'], StringSplitOptions.RemoveEmptyEntries).Select(word => word.TrimEnd('s'))];

    /// <summary>The Levenshtein distance: how many letters to insert, delete or change to make one name the other.</summary>
    private static int Edits(string from, string to)
    {
        var previous = new int[to.Length + 1];
        var current = new int[to.Length + 1];

        for (var column = 0; column <= to.Length; column++)
            previous[column] = column;

        for (var row = 1; row <= from.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= to.Length; column++)
            {
                var change = from[row - 1] == to[column - 1] ? 0 : 1;

                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + change);
            }

            (previous, current) = (current, previous);
        }

        return previous[to.Length];
    }
}
