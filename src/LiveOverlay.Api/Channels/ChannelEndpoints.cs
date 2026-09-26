using System.Security.Claims;
using LiveOverlay.Api.Data;
using LiveOverlay.Api.Events;
using LiveOverlay.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LiveOverlay.Api.Channels;

public sealed record CreateChannelRequest(string? Name, string? Currency);

/// <summary>The only response that ever contains the keys. They are stored as hashes and cannot be shown again.</summary>
public sealed record CreatedChannel(Guid Id, string Name, string Currency, string DashboardKey, string OverlayToken);

public sealed record ChannelSummary(Guid Id, string Name, string Currency, long LastSequence, GoalSnapshot Goal);

public static class ChannelEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string DashboardPolicy = "Dashboard";

    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapPost("/channels", CreateChannel);

        var channel = api.MapGroup("/channel").RequireAuthorization(DashboardPolicy);
        channel.MapGet("", GetChannel);
        channel.MapPost("/events", PublishEvent);
        channel.MapPut("/goal", SetGoal);

        return app;
    }

    private static async Task<IResult> CreateChannel(CreateChannelRequest request, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        var currency = request.Currency?.Trim().ToUpperInvariant();
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrEmpty(name) || name.Length > 60)
        {
            errors["name"] = ["Name is required and at most 60 characters."];
        }

        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            errors["currency"] = ["Currency must be a three-letter ISO 4217 code."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var dashboardKey = ChannelKeys.Generate(ChannelKeys.DashboardPrefix);
        var overlayToken = ChannelKeys.Generate(ChannelKeys.OverlayPrefix);
        var channel = new Channel
        {
            Id = Guid.CreateVersion7(),
            Name = name!,
            Currency = currency!,
            DashboardKeyHash = ChannelKeys.Hash(dashboardKey),
            OverlayTokenHash = ChannelKeys.Hash(overlayToken),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };

        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);

        return Results.Created("/api/channel", new CreatedChannel(channel.Id, channel.Name, channel.Currency, dashboardKey, overlayToken));
    }

    private static async Task<IResult> GetChannel(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var channelId = user.GetChannelId();
        var channel = await db.Channels.AsNoTracking().SingleAsync(c => c.Id == channelId, ct);

        return Results.Ok(new ChannelSummary(
            channel.Id,
            channel.Name,
            channel.Currency,
            channel.LastSequence,
            new GoalSnapshot(channel.GoalTitle, channel.GoalTargetMinor, channel.GoalCurrentMinor)));
    }

    private static async Task<IResult> PublishEvent(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        PublishEventRequest request,
        ClaimsPrincipal user,
        EventPublisher publisher,
        CancellationToken ct)
    {
        var errors = EventValidation.Validate(request);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > EventValidation.MaxIdempotencyKey)
        {
            errors[IdempotencyKeyHeader] = [$"An {IdempotencyKeyHeader} header of at most {EventValidation.MaxIdempotencyKey} characters is required, so a retried request is stored once."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var result = await publisher.PublishAsync(
            user.GetChannelId(),
            idempotencyKey!,
            new NewEvent(request.Type, request.DisplayName!.Trim(), request.AmountMinor, NullIfBlank(request.Message)),
            ct);

        return ToHttpResult(result);
    }

    private static async Task<IResult> SetGoal(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        SetGoalRequest request,
        ClaimsPrincipal user,
        EventPublisher publisher,
        CancellationToken ct)
    {
        var errors = EventValidation.Validate(request);
        if (idempotencyKey?.Length > EventValidation.MaxIdempotencyKey)
        {
            errors[IdempotencyKeyHeader] = [$"{IdempotencyKeyHeader} is at most {EventValidation.MaxIdempotencyKey} characters."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // Replacing a goal is idempotent by nature, so the key is optional here.
        var result = await publisher.PublishAsync(
            user.GetChannelId(),
            string.IsNullOrWhiteSpace(idempotencyKey) ? $"goal-{Guid.NewGuid():N}" : idempotencyKey,
            new NewEvent(EventType.GoalSet, request.Title!.Trim(), null, null, request.TargetMinor),
            ct);

        return ToHttpResult(result);
    }

    private static IResult ToHttpResult(PublishResult result) => result.Outcome switch
    {
        PublishOutcome.Created => Results.Created((string?)null, result.Event),
        PublishOutcome.Duplicate => Results.Ok(result.Event),
        _ => Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Idempotency-Key reused",
            detail: $"This {IdempotencyKeyHeader} was already used for a different request. Use a new key for a new event."),
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
