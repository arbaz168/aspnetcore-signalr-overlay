using LiveOverlay.Api.Events;
using Microsoft.AspNetCore.Hosting;

namespace LiveOverlay.Tests;

public sealed class ResumeAndPresenceTests : IAsyncDisposable
{
    private const int MaxReplay = 5;

    private readonly OverlayAppFactory _app = new(b => b.UseSetting("Overlay:MaxReplayEvents", MaxReplay.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task ReconnectingOverlay_GetsWhatItMissed_InOrder()
    {
        var channel = await _app.CreateChannelAsync();
        await _app.DonateAsync(channel.DashboardKey, 100);

        await using (var overlay = await _app.ConnectAsync(channel.OverlayToken))
        {
            var first = await overlay.ResumeAsync(0);
            Assert.Equal([1L], first.Events.Select(e => e.Sequence));
        }

        // Offline while three more arrive.
        await _app.DonateAsync(channel.DashboardKey, 200);
        await _app.DonateAsync(channel.DashboardKey, 300);
        await _app.DonateAsync(channel.DashboardKey, 400);

        await using var reconnected = await _app.ConnectAsync(channel.OverlayToken);
        var resume = await reconnected.ResumeAsync(afterSequence: 1);

        Assert.Equal([2L, 3L, 4L], resume.Events.Select(e => e.Sequence));
        Assert.Equal(4, resume.LatestSequence);
        Assert.Equal(0, resume.SkippedCount);
        Assert.Equal(1_000, resume.Goal.CurrentMinor);
    }

    [Fact]
    public async Task StaleEvents_AreNotReplayed_ButTheGoalIsStillCurrent()
    {
        var channel = await _app.CreateChannelAsync();
        await _app.DonateAsync(channel.DashboardKey, 500);
        _app.Time.Advance(TimeSpan.FromMinutes(10));
        await _app.DonateAsync(channel.DashboardKey, 200);

        await using var overlay = await _app.ConnectAsync(channel.OverlayToken);
        var resume = await overlay.ResumeAsync(0);

        Assert.Equal([2L], resume.Events.Select(e => e.Sequence));
        Assert.Equal(1, resume.SkippedCount);
        Assert.Equal(2, resume.LatestSequence);
        Assert.Equal(700, resume.Goal.CurrentMinor);
    }

    [Fact]
    public async Task Replay_IsCappedToTheNewestEvents()
    {
        var channel = await _app.CreateChannelAsync();
        for (var i = 1; i <= 8; i++)
        {
            await _app.DonateAsync(channel.DashboardKey, i);
        }

        await using var overlay = await _app.ConnectAsync(channel.OverlayToken);
        var resume = await overlay.ResumeAsync(0);

        Assert.Equal([4L, 5L, 6L, 7L, 8L], resume.Events.Select(e => e.Sequence));
        Assert.Equal(3, resume.SkippedCount);
    }

    [Fact]
    public async Task ClientAheadOfTheServer_GetsNothing_AndIsToldTheRealLatestSequence()
    {
        // For example after the database was reset. The client must trust LatestSequence, not its own counter.
        var channel = await _app.CreateChannelAsync();
        await _app.DonateAsync(channel.DashboardKey, 100);

        await using var overlay = await _app.ConnectAsync(channel.OverlayToken);
        var resume = await overlay.ResumeAsync(afterSequence: 99);

        Assert.Empty(resume.Events);
        Assert.Equal(1, resume.LatestSequence);
        Assert.Equal(0, resume.SkippedCount);
    }

    [Fact]
    public async Task Dashboard_SeesOverlaysConnectAndDisconnect()
    {
        var channel = await _app.CreateChannelAsync();
        await using var dashboard = await _app.ConnectAsync(channel.DashboardKey);
        Assert.Equal(0, (await dashboard.ResumeAsync(0)).ConnectedOverlays);

        var overlay = await _app.ConnectAsync(channel.OverlayToken);
        Assert.Equal(1, await dashboard.NextOverlayCountAsync());

        await using (var second = await _app.ConnectAsync(channel.OverlayToken))
        {
            Assert.Equal(2, await dashboard.NextOverlayCountAsync());
        }

        Assert.Equal(1, await dashboard.NextOverlayCountAsync());

        await overlay.DisposeAsync();
        Assert.Equal(0, await dashboard.NextOverlayCountAsync());
    }

    [Fact]
    public async Task EveryEvent_CarriesTheGoalAsItStoodAfterIt()
    {
        var channel = await _app.CreateChannelAsync();
        await _app.DonateAsync(channel.DashboardKey, 100);
        await _app.DonateAsync(channel.DashboardKey, 250);

        await using var overlay = await _app.ConnectAsync(channel.OverlayToken);
        var resume = await overlay.ResumeAsync(0);

        Assert.Equal([100L, 350L], resume.Events.Select(e => e.Goal.CurrentMinor));
    }
}
