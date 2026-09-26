using System.Collections.Concurrent;

namespace LiveOverlay.Api.Realtime;

/// <summary>
/// Counts connected overlays per channel for the dashboard. In memory, so it is correct for one instance only;
/// scaling out would move this count to the backplane (for example a Redis hash keyed by channel).
/// </summary>
public sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<Guid, int> _overlays = new();

    public int Connected(Guid channelId) => _overlays.AddOrUpdate(channelId, 1, (_, count) => count + 1);

    public int Disconnected(Guid channelId) => _overlays.AddOrUpdate(channelId, 0, (_, count) => Math.Max(0, count - 1));

    public int Count(Guid channelId) => _overlays.GetValueOrDefault(channelId);
}
