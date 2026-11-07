namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>How much one <c>run_tools</c> call may do and bring back.</summary>
public sealed class SequenceOptions
{
    /// <summary>The most steps one call runs; twenty unless set.</summary>
    /// <remarks>The limit of Snail.MCP.FileSystem. A longer run is several calls, so one mistake does not cost a hundred steps.</remarks>
    public int MaxSteps { get; set; } = 20;

    /// <summary>The characters of step answers one call brings back; fifty thousand unless set.</summary>
    /// <remarks>
    /// The budget of Snail.MCP.Roslyn. An answer past it is left out and its step flagged <c>isOmitted</c>, which says the
    /// step ran and how to read its answer — call that tool alone — instead of filling the window with it.
    /// </remarks>
    public int OutputBudget { get; set; } = 50_000;
}
