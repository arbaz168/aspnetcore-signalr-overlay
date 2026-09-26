using System.Net;
using System.Net.Http.Json;
using LiveOverlay.Api.Channels;
using LiveOverlay.Api.Events;
using Microsoft.EntityFrameworkCore;

namespace LiveOverlay.Tests;

public sealed class EventPublishingTests : IAsyncDisposable
{
    private readonly OverlayAppFactory _app = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task PublishedEvents_ReachOverlaysInOrder_WithConsecutiveSequenceNumbers()
    {
        var channel = await _app.CreateChannelAsync();
        await using var overlay = await _app.ConnectAsync(channel.OverlayToken);

        await _app.PublishAsync(channel.DashboardKey, OverlayAppFactory.Follow("Alex"));
        await _app.DonateAsync(channel.DashboardKey, 500, "Sam");
        await _app.PublishAsync(channel.DashboardKey, new { type = "Subscription", displayName = "Kai" });

        var received = new[] { await overlay.NextEventAsync(), await overlay.NextEventAsync(), await overlay.NextEventAsync() };
        Assert.Equal([1L, 2L, 3L], received.Select(e => e.Sequence));
        Assert.Equal([EventType.Follow, EventType.Donation, EventType.Subscription], received.Select(e => e.Type));
        Assert.Equal("GBP", received[1].Currency);
    }

    [Fact]
    public async Task OverlayOnAnotherChannel_ReceivesNothing()
    {
        var mine = await _app.CreateChannelAsync("Mine");
        var theirs = await _app.CreateChannelAsync("Theirs");
        await using var theirOverlay = await _app.ConnectAsync(theirs.OverlayToken);

        await _app.DonateAsync(mine.DashboardKey, 500);

        await theirOverlay.AssertNoEventAsync();
    }

    [Fact]
    public async Task Dashboard_ReceivesEventsToo()
    {
        var channel = await _app.CreateChannelAsync();
        await using var dashboard = await _app.ConnectAsync(channel.DashboardKey);

        await _app.DonateAsync(channel.DashboardKey, 250);

        Assert.Equal(250, (await dashboard.NextEventAsync()).AmountMinor);
    }

    [Fact]
    public async Task SameRequestRetriedWithTheSameKey_IsStoredAndBroadcastOnce()
    {
        var channel = await _app.CreateChannelAsync();
        await using var overlay = await _app.ConnectAsync(channel.OverlayToken);

        var first = await _app.PublishAsync(channel.DashboardKey, OverlayAppFactory.Donation(500), "retry-me");
        var retry = await _app.PublishAsync(channel.DashboardKey, OverlayAppFactory.Donation(500), "retry-me");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var firstEvent = await first.Content.ReadFromJsonAsync<EventEnvelope>(OverlayAppFactory.ApiJson, Ct);
        var retryEvent = await retry.Content.ReadFromJsonAsync<EventEnvelope>(OverlayAppFactory.ApiJson, Ct);
        Assert.Equal(firstEvent!.Id, retryEvent!.Id);

        Assert.Equal(1, (await overlay.NextEventAsync()).Sequence);
        await overlay.AssertNoEventAsync();
        Assert.Equal(1, await _app.QueryAsync(db => db.Events.CountAsync(Ct)));
        Assert.Equal(500, await _app.QueryAsync(db => db.Channels.Select(c => c.GoalCurrentMinor).SingleAsync(Ct)));
    }

    [Fact]
    public async Task KeyReusedForADifferentEvent_IsRejected_AndNothingIsStored()
    {
        var channel = await _app.CreateChannelAsync();
        await _app.PublishAsync(channel.DashboardKey, OverlayAppFactory.Donation(500), "key-1");

        var reused = await _app.PublishAsync(channel.DashboardKey, OverlayAppFactory.Donation(9_000), "key-1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Equal(1, await _app.QueryAsync(db => db.Events.CountAsync(Ct)));
    }

    [Fact]
    public async Task DuplicateRequest_DoesNotUseUpASequenceNumber()
    {
        var channel = await _app.CreateChannelAsync();

        await _app.DonateAsync(channel.DashboardKey, 100, idempotencyKey: "a");
        await _app.DonateAsync(channel.DashboardKey, 100, idempotencyKey: "a");
        var next = await _app.DonateAsync(channel.DashboardKey, 100, idempotencyKey: "b");

        Assert.Equal(2, next.Sequence);
    }

    [Fact]
    public async Task ConcurrentPublishers_GetUniqueGapFreeSequenceNumbers()
    {
        const int publishers = 25;
        var channel = await _app.CreateChannelAsync();

        var responses = await Task.WhenAll(Enumerable.Range(1, publishers)
            .Select(i => _app.PublishAsync(channel.DashboardKey, OverlayAppFactory.Donation(i))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var sequences = await _app.QueryAsync(db => db.Events.OrderBy(e => e.Sequence).Select(e => e.Sequence).ToListAsync(Ct));
        Assert.Equal(Enumerable.Range(1, publishers).Select(i => (long)i), sequences);

        var channelRow = await _app.QueryAsync(db => db.Channels.AsNoTracking().SingleAsync(Ct));
        Assert.Equal(publishers, channelRow.LastSequence);
        Assert.Equal(Enumerable.Range(1, publishers).Sum(), channelRow.GoalCurrentMinor);
    }

    [Fact]
    public async Task ConcurrentRetriesOfOneRequest_AreStoredOnce()
    {
        var channel = await _app.CreateChannelAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => _app.PublishAsync(channel.DashboardKey, OverlayAppFactory.Donation(500), "same-key")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
        Assert.Equal(1, await _app.QueryAsync(db => db.Events.CountAsync(Ct)));
        Assert.Equal(500, await _app.QueryAsync(db => db.Channels.Select(c => c.GoalCurrentMinor).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Donations_MoveTheGoal_OtherEventsDoNot()
    {
        var channel = await _app.CreateChannelAsync();
        var dashboard = _app.CreateClient(channel.DashboardKey);
        (await dashboard.PutAsJsonAsync("/api/channel/goal", new { title = "New microphone", targetMinor = 10_000 }, Ct)).EnsureSuccessStatusCode();

        await _app.DonateAsync(channel.DashboardKey, 1_500);
        await _app.PublishAsync(channel.DashboardKey, OverlayAppFactory.Follow());
        var last = await _app.DonateAsync(channel.DashboardKey, 500);

        Assert.Equal(new GoalSnapshot("New microphone", 10_000, 2_000), last.Goal);
    }

    [Fact]
    public async Task SettingAGoal_StartsFromZero_AndIsBroadcastLikeAnyOtherEvent()
    {
        var channel = await _app.CreateChannelAsync();
        await _app.DonateAsync(channel.DashboardKey, 700);
        await using var overlay = await _app.ConnectAsync(channel.OverlayToken);

        var response = await _app.CreateClient(channel.DashboardKey)
            .PutAsJsonAsync("/api/channel/goal", new { title = "Charity stream", targetMinor = 50_000 }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var received = await overlay.NextEventAsync();
        Assert.Equal(EventType.GoalSet, received.Type);
        Assert.Equal(2, received.Sequence);
        Assert.Equal(new GoalSnapshot("Charity stream", 50_000, 0), received.Goal);
    }

    [Fact]
    public async Task MissingIdempotencyKey_IsRejected()
    {
        var channel = await _app.CreateChannelAsync();

        var response = await _app.CreateClient(channel.DashboardKey)
            .PostAsJsonAsync("/api/channel/events", OverlayAppFactory.Follow(), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(ChannelEndpoints.IdempotencyKeyHeader, await response.Content.ReadAsStringAsync(Ct));
    }

    public static TheoryData<string, object> InvalidEvents => new()
    {
        { "donation without an amount", new { type = "Donation", displayName = "Sam" } },
        { "negative donation", new { type = "Donation", displayName = "Sam", amountMinor = -5 } },
        { "follow with an amount", new { type = "Follow", displayName = "Sam", amountMinor = 100 } },
        { "blank name", new { type = "Follow", displayName = "   " } },
        { "name too long", new { type = "Follow", displayName = new string('x', 41) } },
        { "message too long", new { type = "Donation", displayName = "Sam", amountMinor = 100, message = new string('x', 201) } },
        { "goal through the events endpoint", new { type = "GoalSet", displayName = "Sneaky" } },
        { "unknown type", new { type = "Raid", displayName = "Sam" } },
    };

    [Theory]
    [MemberData(nameof(InvalidEvents))]
    public async Task InvalidEvent_IsRejected_AndNothingIsStored(string reason, object body)
    {
        var channel = await _app.CreateChannelAsync();

        var response = await _app.PublishAsync(channel.DashboardKey, body);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, reason);
        Assert.Equal(0, await _app.QueryAsync(db => db.Events.CountAsync(Ct)));
    }
}
