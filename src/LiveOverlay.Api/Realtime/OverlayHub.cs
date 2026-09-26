using LiveOverlay.Api.Data;
using LiveOverlay.Api.Events;
using LiveOverlay.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LiveOverlay.Api.Realtime;

public interface IOverlayClient
{
    Task EventPublished(EventEnvelope envelope);

    /// <summary>Sent to dashboards only.</summary>
    Task OverlaysChanged(int connectedOverlays);
}

/// <summary>
/// Overlays and dashboards both connect here. Neither can publish through the hub: publishing is an HTTP call
/// with the dashboard key, so a leaked overlay token can only read.
/// </summary>
[Authorize]
public sealed class OverlayHub(
    AppDbContext db,
    PresenceTracker presence,
    IOptions<OverlayOptions> options,
    TimeProvider time) : Hub<IOverlayClient>
{
    public const string Path = "/hubs/overlay";

    public static string ChannelGroup(Guid channelId) => $"channel:{channelId}";

    public static string DashboardGroup(Guid channelId) => $"dashboard:{channelId}";

    public override async Task OnConnectedAsync()
    {
        var channelId = Context.User!.GetChannelId();
        await Groups.AddToGroupAsync(Context.ConnectionId, ChannelGroup(channelId));

        if (Context.User!.IsDashboard())
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup(channelId));
        }
        else
        {
            await Clients.Group(DashboardGroup(channelId)).OverlaysChanged(presence.Connected(channelId));
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (!Context.User!.IsDashboard())
        {
            var channelId = Context.User!.GetChannelId();
            await Clients.Group(DashboardGroup(channelId)).OverlaysChanged(presence.Disconnected(channelId));
        }
    }

    /// <summary>
    /// Called on every connect and reconnect with the last sequence the client applied (0 when it has none).
    /// Returns what was missed, but only recent events: an overlay coming back after an hour should not
    /// replay an hour of alerts on stream. The goal is state, not history, so it is always current.
    /// </summary>
    public async Task<ResumeResult> Resume(long afterSequence)
    {
        var channelId = Context.User!.GetChannelId();
        var settings = options.Value;
        var ct = Context.ConnectionAborted;

        var channel = await db.Channels.AsNoTracking().SingleAsync(c => c.Id == channelId, ct);
        var cutoff = time.GetUtcNow().UtcDateTime - settings.ReplayWindow;

        var recent = await db.Events.AsNoTracking()
            .Where(e => e.ChannelId == channelId && e.Sequence > afterSequence && e.CreatedAt >= cutoff)
            .OrderByDescending(e => e.Sequence)
            .Take(settings.MaxReplayEvents)
            .ToListAsync(ct);

        var events = recent.OrderBy(e => e.Sequence).Select(e => EventEnvelope.From(e, channel.Currency)).ToList();
        var missed = Math.Max(0, channel.LastSequence - afterSequence);

        return new ResumeResult(
            events,
            channel.LastSequence,
            missed - events.Count,
            channel.Currency,
            new GoalSnapshot(channel.GoalTitle, channel.GoalTargetMinor, channel.GoalCurrentMinor),
            presence.Count(channelId));
    }
}
