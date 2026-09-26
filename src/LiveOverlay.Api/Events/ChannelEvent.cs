namespace LiveOverlay.Api.Events;

public enum EventType
{
    Follow,
    Subscription,
    Donation,

    /// <summary>A new goal was set. <c>DisplayName</c> holds its title, and progress starts again from zero.</summary>
    GoalSet,
}

/// <summary>
/// Every change an overlay needs to see is one of these, in one ordered stream per channel.
/// That keeps recovery simple: a client that knows its last sequence number knows exactly what it missed.
/// </summary>
public sealed class ChannelEvent
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }

    public long Sequence { get; set; }

    /// <summary>Supplied by the publisher so a retried request is stored once.</summary>
    public required string IdempotencyKey { get; set; }

    public EventType Type { get; set; }

    public required string DisplayName { get; set; }

    public long? AmountMinor { get; set; }

    public string? Message { get; set; }

    /// <summary>The goal as it stood after this event, so every event is self-contained for a late client.</summary>
    public string? GoalTitle { get; set; }

    public long GoalTargetMinor { get; set; }

    public long GoalCurrentMinor { get; set; }

    public DateTime CreatedAt { get; set; }
}
