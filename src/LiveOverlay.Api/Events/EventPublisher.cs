using LiveOverlay.Api.Data;
using LiveOverlay.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LiveOverlay.Api.Events;

public enum PublishOutcome
{
    Created,

    /// <summary>Same Idempotency-Key and same body as before. The original event is returned and nothing is sent again.</summary>
    Duplicate,

    /// <summary>The Idempotency-Key was already used for a different request. Nothing is stored.</summary>
    KeyReused,
}

public sealed record PublishResult(PublishOutcome Outcome, EventEnvelope? Event);

public sealed record NewEvent(EventType Type, string DisplayName, long? AmountMinor, string? Message, long? GoalTargetMinor = null);

/// <summary>
/// Stores an event with the next sequence number for its channel, applies it to the goal, then broadcasts it.
/// </summary>
public sealed class EventPublisher(
    AppDbContext db,
    IHubContext<OverlayHub, IOverlayClient> hub,
    TimeProvider time,
    ILogger<EventPublisher> logger)
{
    public async Task<PublishResult> PublishAsync(Guid channelId, string idempotencyKey, NewEvent newEvent, CancellationToken ct)
    {
        EventEnvelope envelope;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            // Incrementing the counter first takes the channel's write lock for the rest of the transaction.
            // Concurrent publishers queue behind it instead of racing, so the sequence is gap-free and the
            // idempotency check below is safe without a retry loop. A rollback hands the number back.
            var sequences = await db.Database
                .SqlQuery<long>($"UPDATE Channels SET LastSequence = LastSequence + 1 WHERE Id = {channelId} RETURNING LastSequence AS Value")
                .ToListAsync(ct);

            if (sequences.Count == 0)
            {
                throw new InvalidOperationException($"Channel {channelId} does not exist.");
            }

            var existing = await db.Events.AsNoTracking()
                .FirstOrDefaultAsync(e => e.ChannelId == channelId && e.IdempotencyKey == idempotencyKey, ct);

            if (existing is not null)
            {
                await tx.RollbackAsync(ct);
                var currency = await db.Channels.Where(c => c.Id == channelId).Select(c => c.Currency).SingleAsync(ct);

                return SameRequest(existing, newEvent)
                    ? new PublishResult(PublishOutcome.Duplicate, EventEnvelope.From(existing, currency))
                    : new PublishResult(PublishOutcome.KeyReused, null);
            }

            var channel = await db.Channels.SingleAsync(c => c.Id == channelId, ct);

            switch (newEvent.Type)
            {
                case EventType.Donation:
                    channel.GoalCurrentMinor += newEvent.AmountMinor ?? 0;
                    break;
                case EventType.GoalSet:
                    channel.GoalTitle = newEvent.DisplayName;
                    channel.GoalTargetMinor = newEvent.GoalTargetMinor ?? 0;
                    channel.GoalCurrentMinor = 0;
                    break;
            }

            var stored = new ChannelEvent
            {
                Id = Guid.CreateVersion7(),
                ChannelId = channelId,
                Sequence = sequences[0],
                IdempotencyKey = idempotencyKey,
                Type = newEvent.Type,
                DisplayName = newEvent.DisplayName,
                AmountMinor = newEvent.AmountMinor,
                Message = newEvent.Message,
                GoalTitle = channel.GoalTitle,
                GoalTargetMinor = channel.GoalTargetMinor,
                GoalCurrentMinor = channel.GoalCurrentMinor,
                CreatedAt = time.GetUtcNow().UtcDateTime,
            };

            db.Events.Add(stored);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            envelope = EventEnvelope.From(stored, channel.Currency);
        }

        // Sent only after the commit, so no client ever sees an event that was rolled back. If this send is
        // lost (crash, dropped connection), clients notice the gap at the next event and fetch what they missed.
        try
        {
            await hub.Clients.Group(OverlayHub.ChannelGroup(channelId)).EventPublished(envelope);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Broadcast of event {Sequence} on channel {ChannelId} failed; clients recover it on resume",
                envelope.Sequence, channelId);
        }

        logger.LogInformation("Published {EventType} as event {Sequence} on channel {ChannelId}", envelope.Type, envelope.Sequence, channelId);

        return new PublishResult(PublishOutcome.Created, envelope);
    }

    private static bool SameRequest(ChannelEvent existing, NewEvent request) =>
        existing.Type == request.Type
        && existing.DisplayName == request.DisplayName
        && existing.AmountMinor == request.AmountMinor
        && existing.Message == request.Message
        && (request.Type != EventType.GoalSet || existing.GoalTargetMinor == request.GoalTargetMinor);
}
