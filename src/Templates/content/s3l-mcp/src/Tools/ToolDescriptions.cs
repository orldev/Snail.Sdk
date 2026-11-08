namespace Snail.MCP.Template.Tools;

/// <summary>What a model reads about the tools: instructions it acts on, changed with care, apart from the words a person reads.</summary>
/// <remarks>Attributes take constants, so every description is static and a guard test can hold its words to the code.</remarks>
internal static class ToolDescriptions
{
    public const string Diagnose = "Say what state this server is in: its name, its version and the runtime it runs on. Changes nothing.";
#if (jobs)

    public const string Work = "Count to a number, a step every tenth of a second, as a job: answers with the count when it ends within the wait, or with the running job to read as a resource.";

    /// <summary>The limit is <see cref="WorkTools.MostSteps"/>; a guard test holds the words to it.</summary>
    public const string WorkUpTo = "The number to count to, 1 to 600.";

    public const string WorkWait = "Seconds to wait for the count before answering with the job in flight; 0 answers at once.";
#endif
}
