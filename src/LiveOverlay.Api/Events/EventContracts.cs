namespace LiveOverlay.Api.Events;

public sealed record PublishEventRequest(EventType Type, string? DisplayName, long? AmountMinor, string? Message);

public sealed record SetGoalRequest(string? Title, long TargetMinor);

public sealed record GoalSnapshot(string? Title, long TargetMinor, long CurrentMinor);

/// <summary>What overlays and dashboards receive, live or on resume.</summary>
public sealed record EventEnvelope(
    Guid Id,
    long Sequence,
    EventType Type,
    string DisplayName,
    long? AmountMinor,
    string Currency,
    string? Message,
    DateTimeOffset CreatedAt,
    GoalSnapshot Goal)
{
    public static EventEnvelope From(ChannelEvent e, string currency) => new(
        e.Id,
        e.Sequence,
        e.Type,
        e.DisplayName,
        e.AmountMinor,
        currency,
        e.Message,
        new DateTimeOffset(e.CreatedAt, TimeSpan.Zero),
        new GoalSnapshot(e.GoalTitle, e.GoalTargetMinor, e.GoalCurrentMinor));
}

/// <summary>
/// Returned to a client that connects or reconnects. <see cref="LatestSequence"/> is authoritative: the client
/// moves to it after applying <see cref="Events"/>, even when older events were skipped as stale.
/// </summary>
public sealed record ResumeResult(
    IReadOnlyList<EventEnvelope> Events,
    long LatestSequence,
    long SkippedCount,
    string Currency,
    GoalSnapshot Goal,
    int ConnectedOverlays);

public static class EventValidation
{
    public const int MaxDisplayName = 40;
    public const int MaxMessage = 200;
    public const int MaxGoalTitle = 60;
    public const int MaxIdempotencyKey = 100;
    public const long MaxDonationMinor = 10_000_000;
    public const long MaxGoalMinor = 1_000_000_000;

    public static Dictionary<string, string[]> Validate(PublishEventRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Type == EventType.GoalSet)
        {
            errors["type"] = ["Set a goal with PUT /api/channel/goal."];
        }

        var name = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxDisplayName)
        {
            errors["displayName"] = [$"Display name is required and at most {MaxDisplayName} characters."];
        }

        if (request.Message?.Length > MaxMessage)
        {
            errors["message"] = [$"Message is at most {MaxMessage} characters."];
        }

        if (request.Type == EventType.Donation)
        {
            if (request.AmountMinor is not (> 0 and <= MaxDonationMinor))
            {
                errors["amountMinor"] = [$"A donation needs an amount between 1 and {MaxDonationMinor} minor units."];
            }
        }
        else if (request.AmountMinor is not null)
        {
            errors["amountMinor"] = ["Only donations carry an amount."];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(SetGoalRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > MaxGoalTitle)
        {
            errors["title"] = [$"Title is required and at most {MaxGoalTitle} characters."];
        }

        if (request.TargetMinor is not (> 0 and <= MaxGoalMinor))
        {
            errors["targetMinor"] = [$"Target must be between 1 and {MaxGoalMinor} minor units."];
        }

        return errors;
    }
}
