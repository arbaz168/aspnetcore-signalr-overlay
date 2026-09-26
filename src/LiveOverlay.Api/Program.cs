using System.Text.Json.Serialization;
using LiveOverlay.Api;
using LiveOverlay.Api.Channels;
using LiveOverlay.Api.Data;
using LiveOverlay.Api.Realtime;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLiveOverlay();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();

await using (var scope = app.Services.CreateAsyncScope())
{
    // Convenient for a demo. A production release would apply migrations from its deploy pipeline.
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // WAL lets readers carry on while a publisher holds the write lock.
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
}

// The built React app (npm run build in web/) is served from wwwroot: the dashboard at / and the overlay at /overlay.html.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapChannelEndpoints();
app.MapHub<OverlayHub>(OverlayHub.Path);

await app.RunAsync();
