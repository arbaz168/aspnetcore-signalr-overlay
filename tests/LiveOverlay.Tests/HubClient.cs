using System.Threading.Channels;
using LiveOverlay.Api.Events;
using Microsoft.AspNetCore.SignalR.Client;

namespace LiveOverlay.Tests;

/// <summary>A connected hub client that records what the server pushes to it.</summary>
public sealed class HubClient : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Channel<EventEnvelope> _events = Channel.CreateUnbounded<EventEnvelope>();
    private readonly Channel<int> _overlayCounts = Channel.CreateUnbounded<int>();

    public HubClient(HubConnection connection)
    {
        Connection = connection;
        connection.On<EventEnvelope>("EventPublished", e => _events.Writer.TryWrite(e));
        connection.On<int>("OverlaysChanged", count => _overlayCounts.Writer.TryWrite(count));
    }

    public HubConnection Connection { get; }

    public Task<ResumeResult> ResumeAsync(long afterSequence) =>
        Connection.InvokeAsync<ResumeResult>("Resume", afterSequence, TestContext.Current.CancellationToken);

    public Task<EventEnvelope> NextEventAsync() => ReadAsync(_events);

    public Task<int> NextOverlayCountAsync() => ReadAsync(_overlayCounts);

    /// <summary>Waits briefly and fails if anything arrived. Long enough for a broadcast over the in-memory server.</summary>
    public async Task AssertNoEventAsync()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        Assert.False(_events.Reader.TryRead(out var unexpected), $"Unexpected event {unexpected?.Sequence}");
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    private static async Task<T> ReadAsync<T>(Channel<T> channel)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);
        return await channel.Reader.ReadAsync(cts.Token);
    }
}
