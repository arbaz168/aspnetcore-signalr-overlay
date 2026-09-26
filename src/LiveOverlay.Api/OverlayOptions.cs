namespace LiveOverlay.Api;

public sealed class OverlayOptions
{
    public const string SectionName = "Overlay";

    /// <summary>Events older than this are not replayed to a reconnecting client. They are stale on stream.</summary>
    public TimeSpan ReplayWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Upper bound on events returned by one resume, newest first.</summary>
    public int MaxReplayEvents { get; set; } = 50;
}
