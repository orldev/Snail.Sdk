namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>What a model reads about the tools discovery adds: instructions it acts on, changed with care.</summary>
internal static class ToolDescriptions
{
    public const string FindTool =
        "Finds this server's tools for a task described in words, with what each takes, enough to call it at once. "
        + "Answers the best matches first; when nothing matches, every tool, with isMatched false. "
        + "Ask it before concluding that the server has no tool for something.";

    public const string FindToolWithSkills =
        FindTool + " Searches every skill, loaded or not: a tool with isLoaded false joins the tool list once its skill is "
        + "loaded (enable: true, or the enable_skill tool), and runs through run_tools without loading anything.";

    public const string Task = "What you are trying to do, in words: 'change one line', 'push the branch'.";

    public const string Intent = "The same as task, for a caller that names it intent; task wins when both are sent.";

    public const string Limit = "How many tools to name, 1 to 20; five by default.";

    public const string Enable = "Load the skills the tools found belong to, so they join the tool list; false by default.";

    public const string RunTools =
        "Runs several of this server's tools in one call, in order, stopping at the first that fails. "
        + "For steps that do not depend on reading the previous answer. Each step is an ordinary call of that tool under "
        + "the same grant; a tool that runs other tools is not a step. An answer too long for the call is left out and "
        + "its step flagged isOmitted: call that tool alone to read it.";

    public const string Steps =
        "The steps in order: {\"tool\": \"read_file\", \"arguments\": {\"path\": \"src/App.cs\"}}. find_tool says what a tool takes.";

    public const string StepTool = "The tool's name, as the tool list or find_tool gives it.";

    public const string StepArguments = "The tool's arguments, as a direct call of the tool would send them.";

    public const string Skills = "Lists this server's skills — groups of tools loaded on demand — with whether each is loaded and its tools.";

    public const string EnableSkill = "Loads a skill: its tools join this session's tool list.";

    public const string DisableSkill = "Unloads a skill: its tools leave this session's tool list until it is loaded again.";

    public const string SkillName = "The skill's name, as the skills tool lists it.";
}
