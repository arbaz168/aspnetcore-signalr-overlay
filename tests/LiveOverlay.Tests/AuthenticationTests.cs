using System.Net;
using System.Net.Http.Json;
using LiveOverlay.Api.Channels;
using Microsoft.EntityFrameworkCore;

namespace LiveOverlay.Tests;

public sealed class AuthenticationTests : IAsyncDisposable
{
    private readonly OverlayAppFactory _app = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task NewChannel_ReturnsBothKeysOnce_AndStoresOnlyTheirHashes()
    {
        var created = await _app.CreateChannelAsync("Night stream", "usd");

        Assert.StartsWith(ChannelKeys.DashboardPrefix, created.DashboardKey);
        Assert.StartsWith(ChannelKeys.OverlayPrefix, created.OverlayToken);
        Assert.Equal("USD", created.Currency);

        var stored = await _app.QueryAsync(db => db.Channels.AsNoTracking().SingleAsync(Ct));
        Assert.Equal(ChannelKeys.Hash(created.DashboardKey), stored.DashboardKeyHash);
        Assert.Equal(ChannelKeys.Hash(created.OverlayToken), stored.OverlayTokenHash);
        Assert.DoesNotContain(created.DashboardKey, stored.DashboardKeyHash);

        var summary = await _app.CreateClient(created.DashboardKey).GetFromJsonAsync<ChannelSummary>("/api/channel", OverlayAppFactory.ApiJson, Ct);
        Assert.Equal("Night stream", summary!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dk_not-a-real-key")]
    public async Task MissingOrUnknownKey_IsUnauthorized(string? key)
    {
        await _app.CreateChannelAsync();
        var client = key is null ? _app.CreateClient() : _app.CreateClient(key);

        var response = await client.GetAsync("/api/channel", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OverlayToken_CannotPublishOrChangeTheGoal()
    {
        var channel = await _app.CreateChannelAsync();

        var publish = await _app.PublishAsync(channel.OverlayToken, OverlayAppFactory.Follow());
        var goal = await _app.CreateClient(channel.OverlayToken).PutAsJsonAsync("/api/channel/goal", new { title = "Mine now", targetMinor = 1 }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, goal.StatusCode);
        Assert.Equal(0, await _app.QueryAsync(db => db.Events.CountAsync(Ct)));
    }

    [Fact]
    public async Task KeyInTheQueryString_IsIgnoredOutsideTheHub()
    {
        var channel = await _app.CreateChannelAsync();

        var response = await _app.CreateClient().GetAsync($"/api/channel?access_token={channel.DashboardKey}", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hub_AcceptsTheTokenInTheQueryString_BecauseBrowserWebSocketsCannotSendHeaders()
    {
        var channel = await _app.CreateChannelAsync();
        var client = _app.CreateClient();

        var withToken = await client.PostAsync($"/hubs/overlay/negotiate?negotiateVersion=1&access_token={channel.OverlayToken}", null, Ct);
        var without = await client.PostAsync("/hubs/overlay/negotiate?negotiateVersion=1", null, Ct);

        Assert.Equal(HttpStatusCode.OK, withToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, without.StatusCode);
    }

    [Fact]
    public async Task HubConnection_WithAnUnknownToken_IsRefused()
    {
        await _app.CreateChannelAsync();

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => _app.ConnectAsync("ot_not-a-real-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Theory]
    [InlineData("", "GBP")]
    [InlineData("Stream", "GB")]
    [InlineData("Stream", "£££")]
    public async Task InvalidChannel_IsRejected(string name, string currency)
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/api/channels", new { name, currency }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
