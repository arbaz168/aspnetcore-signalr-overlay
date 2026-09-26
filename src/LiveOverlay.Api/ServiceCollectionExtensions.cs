using System.Text.Json.Serialization;
using LiveOverlay.Api.Channels;
using LiveOverlay.Api.Data;
using LiveOverlay.Api.Events;
using LiveOverlay.Api.Realtime;
using LiveOverlay.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiveOverlay.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLiveOverlay(this IServiceCollection services)
    {
        services.AddOptions<OverlayOptions>()
            .BindConfiguration(OverlayOptions.SectionName)
            .Validate(o => o.ReplayWindow > TimeSpan.Zero && o.MaxReplayEvents > 0, "Overlay options are invalid.")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((sp, db) => db.UseSqlite(
            sp.GetRequiredService<IConfiguration>().GetConnectionString("Overlay")
            ?? throw new InvalidOperationException("ConnectionStrings:Overlay is not configured.")));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PresenceTracker>();
        services.AddScoped<EventPublisher>();

        services.AddAuthentication(ChannelKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ChannelKeyAuthenticationHandler>(ChannelKeyAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(ChannelEndpoints.DashboardPolicy, p => p.RequireRole(ChannelRoles.Dashboard));

        services.AddSignalR()
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        return services;
    }
}
