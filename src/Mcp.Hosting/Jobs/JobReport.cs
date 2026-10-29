using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.Sdk.Mcp.Hosting.Jobs;

/// <summary>Where a job stands; written in JSON as the lower-case word, the way a state is read everywhere else.</summary>
[JsonConverter(typeof(JobStateJson))]
public enum JobState
{
    /// <summary>The work is running.</summary>
    Running,

    /// <summary>The work ended with its result.</summary>
    Succeeded,

    /// <summary>The work threw; <see cref="JobReport.Message"/> says what.</summary>
    Failed,

    /// <summary>The work was cancelled, by a tool or because the server stopped.</summary>
    Cancelled,
}

/// <summary>The state as its lower-case word: <c>running</c>, <c>succeeded</c>, <c>failed</c>, <c>cancelled</c>.</summary>
internal sealed class JobStateJson : JsonStringEnumConverter<JobState>
{
    public JobStateJson()
        : base(JsonNamingPolicy.CamelCase)
    {
    }
}

/// <summary>A job as its resource shows it and a client reads it.</summary>
/// <param name="Uri">The resource the job is read and watched at: <c>job://&lt;server&gt;/&lt;kind&gt;/&lt;id&gt;</c>.</param>
/// <param name="State">Where the job stands.</param>
/// <param name="StartedAt">When the work started.</param>
/// <param name="EndedAt">When it ended, once it has.</param>
/// <param name="Progress">How far it has got, 0 to 1, while the work reports it.</param>
/// <param name="Message">The last step the work reported, or why it failed.</param>
/// <param name="Result">The outcome, in the job's own type, once it succeeded.</param>
public sealed record JobReport(
    string Uri,
    JobState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double? Progress,
    string? Message,
    JsonElement? Result)
{
    /// <summary>Whether the work is still running.</summary>
    [JsonIgnore]
    public bool IsRunning => State == JobState.Running;

    /// <summary>The report as its resource carries it: JSON, generated, the same on a native build.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JobJson.Default.JobReport);

    /// <summary>The report read back from a resource's text, or null when the text is not one.</summary>
    public static JobReport? FromJson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            return JsonSerializer.Deserialize(text, JobJson.Default.JobReport);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A step of a job's progress, as the work reports it.</summary>
/// <param name="Done">How much is done, in the units of <paramref name="Total"/> or as a share of 1 when there is none.</param>
/// <param name="Total">How much there is to do, when known.</param>
/// <param name="What">What is being done now.</param>
public readonly record struct JobProgress(double Done, double? Total = null, string? What = null)
{
    /// <summary>The share of the work done, 0 to 1, or null when the step says nothing countable.</summary>
    public double? Share => Total is > 0 ? Math.Clamp(Done / Total.Value, 0, 1) : Done is >= 0 and <= 1 ? Done : null;
}
