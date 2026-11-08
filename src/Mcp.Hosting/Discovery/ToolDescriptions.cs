namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>What a model reads about the tools discovery adds: instructions it acts on, changed with care.</summary>
internal static class ToolDescriptions
{
    public const string FindTool =
        "Finds tools of this server for a task in words, best first, with the parameters of each. "
        + "Ask it before deciding that no tool exists.";

    public const string FindToolWithSkills = FindTool + " It searches unloaded skills too: enable: true loads them.";

    /// <summary>
    /// The description of <c>find_tool</c> with skills, naming the ways the server registered to run a skill's tools
    /// without loading it: <c>run_tools</c>, <c>run_program</c>, both or neither.
    /// </summary>
    public static string FindToolWithSkillsAnd(bool runTools, bool runProgram) => (runTools, runProgram) switch
    {
        (true, true) => $"{FindToolWithSkills[..^1]}, and run_tools or run_program runs their tools without loading.",
        (true, false) => $"{FindToolWithSkills[..^1]}, and run_tools runs their tools without loading.",
        (false, true) => $"{FindToolWithSkills[..^1]}, and run_program calls their commands without loading.",
        _ => FindToolWithSkills,
    };

    public const string Task = "What you are trying to do, in words.";

    public const string Intent = "Same as task.";

    public const string Limit = "How many tools to name, 1 to 20.";

    public const string Enable = "Load the skills of the tools found, so they join the tool list.";

    public const string RunTools =
        "Runs tools of this server in order in one call, stopping at the first failure; no step sees an earlier answer. "
        + "An answer past the budget is flagged isOmitted: call that tool alone.";

    public const string Steps = "The steps, in order.";

    public const string StepTool = "The tool to call.";

    public const string StepArguments = "The arguments of a direct call.";

    public const string Skills = "Lists this server's skills — groups of tools loaded on demand — with whether each is loaded and its tools.";

    public const string EnableSkill = "Loads a skill: its tools join this session's tool list.";

    public const string DisableSkill = "Unloads a skill: its tools leave this session's tool list until it is loaded again.";

    public const string SkillName = "The skill's name, as the skills tool lists it.";

    /// <summary>The description of <c>task</c> with the server's own examples, or without any when it gives none.</summary>
    public static string TaskWith(IEnumerable<string> examples)
    {
        var quoted = examples.Where(example => !string.IsNullOrWhiteSpace(example)).Select(example => $"'{example.Trim()}'").ToList();

        return quoted.Count == 0 ? Task : $"{Task[..^1]}: {string.Join(", ", quoted)}.";
    }
}
