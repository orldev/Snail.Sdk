namespace Snail.Web.Template.Messaging;

/// <summary>A message the application publishes when it greets someone.</summary>
internal sealed record GreetingSent(string Name, DateTimeOffset At);
