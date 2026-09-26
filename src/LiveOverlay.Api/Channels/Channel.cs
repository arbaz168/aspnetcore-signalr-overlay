namespace LiveOverlay.Api.Channels;

public sealed class Channel
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>ISO 4217 code. Amounts on this channel are integer minor units (pence, cents).</summary>
    public required string Currency { get; set; }

    /// <summary>SHA-256 of the key that can publish events and change the goal. The key itself is never stored.</summary>
    public required string DashboardKeyHash { get; set; }

    /// <summary>SHA-256 of the read-only token an overlay connects with.</summary>
    public required string OverlayTokenHash { get; set; }

    /// <summary>Sequence number of the last event published. Incremented under a write lock, so it is gap-free.</summary>
    public long LastSequence { get; set; }

    public string? GoalTitle { get; set; }

    public long GoalTargetMinor { get; set; }

    public long GoalCurrentMinor { get; set; }

    public DateTime CreatedAt { get; set; }
}
