using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveOverlay.Api.Channels;
using LiveOverlay.Api.Data;
using LiveOverlay.Api.Events;
using LiveOverlay.Api.Realtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace LiveOverlay.Tests;

/// <summary>
/// Runs the real app against its own SQLite file with a controllable clock. Hub clients are real SignalR
/// connections over the in-memory test server.
/// </summary>
public sealed class OverlayAppFactory(Action<IWebHostBuilder>? configure = null) : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"live-overlay-{Guid.NewGuid():N}.db");

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Overlay", $"Data Source={_databasePath}");
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
        configure?.Invoke(builder);
    }

    public async Task<CreatedChannel> CreateChannelAsync(string name = "Test channel", string currency = "GBP")
    {
        var response = await CreateClient().PostAsJsonAsync("/api/channels", new { name, currency }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedChannel>(ApiJson, Ct))!;
    }

    public HttpClient CreateClient(string key)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    public async Task<HttpResponseMessage> PublishAsync(string dashboardKey, object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channel/events") { Content = JsonContent.Create(body) };
        request.Headers.Add(ChannelEndpoints.IdempotencyKeyHeader, idempotencyKey ?? Guid.NewGuid().ToString());
        return await CreateClient(dashboardKey).SendAsync(request, Ct);
    }

    public async Task<EventEnvelope> DonateAsync(string dashboardKey, long amountMinor, string name = "Sam", string? idempotencyKey = null)
    {
        var response = await PublishAsync(dashboardKey, Donation(amountMinor, name), idempotencyKey);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EventEnvelope>(ApiJson, Ct))!;
    }

    public static object Donation(long amountMinor, string name = "Sam", string? message = null) =>
        new { type = "Donation", displayName = name, amountMinor, message };

    public static object Follow(string name = "Alex") => new { type = "Follow", displayName = name };

    /// <summary>Connects a real SignalR client. Long polling, because the in-memory test server has no WebSockets.</summary>
    public async Task<HubClient> ConnectAsync(string key)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, OverlayHub.Path.TrimStart('/')), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(key);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        var client = new HubClient(connection);
        await connection.StartAsync(Ct);
        return client;
    }

    public async Task<T> QueryAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_databasePath + suffix);
        }
    }
}
