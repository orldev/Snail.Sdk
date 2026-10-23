using System.ComponentModel.DataAnnotations;

namespace Snail.Web.Template.RateLimiting;

/// <summary>How many requests one client address may make in a window.</summary>
internal sealed class ClientRateLimitOptions
{
    /// <summary>The configuration section the options are read from.</summary>
    public const string SectionName = "RateLimit";

    /// <summary>The requests a client may make in one window.</summary>
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 100;

    /// <summary>The length of the window, in seconds.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}
