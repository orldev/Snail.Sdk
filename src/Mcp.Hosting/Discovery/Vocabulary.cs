using System.Text;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>Words, and how strongly one of them in a tool's name or description answers a task.</summary>
/// <remarks>
/// <para>
/// Split by hand rather than by pattern, so that <c>git_stage</c>, "staged" and "staging" come back to the same word and
/// a task asked in the words of a domain finds the tool named in them. The weights say what they mean: a word in the
/// tool's name, or among the words it is known by, is what the tool is (4); a form of one is nearly that (2.5); a word
/// in its description is only something the tool mentions (1) — the weights five servers of the fleet arrived at.
/// </para>
/// <para>
/// The words every task is made of — what, the, in, my — are dropped, because a description long enough to hold them all
/// would otherwise answer every task ever asked — from a task and from a description alike; so are the words a server
/// names as its noise and the words of its prefix, which every tool's name shares.
/// </para>
/// </remarks>
internal sealed class Vocabulary
{
    private const double InTheName = 4.0;

    private const double LikeTheName = 2.5;

    private const double InTheDescription = 1.0;

    /// <summary>The letters two words must agree on to be forms of each other, and the fewest either may have; below it every word is a form of something.</summary>
    private const int ShortestStem = 4;

    /// <summary>The words a task is made of rather than about.</summary>
    private static readonly string[] Common =
    [
        "a", "all", "an", "and", "any", "are", "as", "at", "be", "but", "by", "can", "do", "does", "for", "from", "has",
        "have", "how", "i", "if", "in", "into", "is", "it", "its", "me", "my", "need", "no", "not", "of", "on", "one", "or",
        "out", "please", "so", "some", "that", "the", "their", "them", "then", "there", "they", "this", "to", "up", "want",
        "was", "what", "when", "where", "which", "who", "why", "will", "with", "would", "you", "your",
    ];

    private readonly HashSet<string> _dropped;

    /// <summary>A vocabulary that drops the common words, the server's noise and the words of its prefix.</summary>
    public Vocabulary(IEnumerable<string> noise, string? prefix)
    {
        _dropped = new HashSet<string>(Common, StringComparer.Ordinal);
        _dropped.UnionWith(noise.SelectMany(Words));
        _dropped.UnionWith(Words(prefix));
    }

    /// <summary>The words of a text, lowercased, with everything that is not a letter or a digit as a boundary.</summary>
    public static IReadOnlyList<string> Words(string? text)
    {
        var words = new List<string>();
        var word = new StringBuilder();

        foreach (var letter in text ?? string.Empty)
        {
            if (char.IsLetterOrDigit(letter))
            {
                word.Append(char.ToLowerInvariant(letter));

                continue;
            }

            if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        return words;
    }

    /// <summary>The words of a task that say something about which tool it wants.</summary>
    public IReadOnlyList<string> Asked(string? task) => [.. Words(task).Where(word => !_dropped.Contains(word)).Distinct(StringComparer.Ordinal)];

    /// <summary>How well a tool answers the asked words, as the sum of what each finds in its name, its other words and its description.</summary>
    public double Score(IReadOnlyList<string> asked, string name, string? meanings, string? description)
    {
        var named = Words(name).Where(word => !_dropped.Contains(word)).ToList();
        var meant = Words(meanings);
        var described = Words(description).Where(word => !_dropped.Contains(word)).ToList();

        return asked.Sum(word => Weight(word, named, meant, described));
    }

    private static double Weight(string asked, IReadOnlyList<string> named, IReadOnlyList<string> meant, IReadOnlyList<string> described)
    {
        if (named.Contains(asked, StringComparer.Ordinal) || meant.Contains(asked, StringComparer.Ordinal))
            return InTheName;

        if (IsFormOfAny(named, asked) || IsFormOfAny(meant, asked))
            return LikeTheName;

        return described.Contains(asked, StringComparer.Ordinal) || IsFormOfAny(described, asked) ? InTheDescription : 0.0;
    }

    private static bool IsFormOfAny(IReadOnlyList<string> words, string asked) => words.Any(word => AreForms(word, asked));

    /// <summary>Whether two words of four letters or more agree on their first four.</summary>
    /// <remarks>
    /// <para>"stage" and "staging" part on the fifth letter, which is why this is not one word starting with the other.</para>
    /// <para>
    /// A shorter word is a form of nothing but itself. Agreeing "entirely when one is shorter", the "s" of "composition's"
    /// was a form of "slerp", the "z" of "+z" of "zzzz" and the "e" and "g" of "e.g." of every word they begin, so a task in
    /// words no tool uses still found tools (Snail.MCP.AfterEffects, Blender) — the fleet's own indexes asked four letters
    /// of both.
    /// </para>
    /// </remarks>
    private static bool AreForms(string word, string asked)
    {
        if (word.Length < ShortestStem || asked.Length < ShortestStem)
            return false;

        return word.AsSpan(0, ShortestStem).SequenceEqual(asked.AsSpan(0, ShortestStem));
    }
}
