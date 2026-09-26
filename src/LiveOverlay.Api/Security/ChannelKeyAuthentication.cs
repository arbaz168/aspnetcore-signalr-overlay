using System.Security.Claims;
using System.Text.Encodings.Web;
using LiveOverlay.Api.Channels;
using LiveOverlay.Api.Data;
using LiveOverlay.Api.Realtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LiveOverlay.Api.Security;

public static class ChannelRoles
{
    public const string Dashboard = "dashboard";
    public const string Overlay = "overlay";
}

public static class ChannelClaims
{
    public const string ChannelId = "channel_id";

    public static Guid GetChannelId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ChannelId) ?? throw new InvalidOperationException("No channel on this principal."));

    public static bool IsDashboard(this ClaimsPrincipal user) => user.IsInRole(ChannelRoles.Dashboard);
}

/// <summary>
/// Authenticates a dashboard key or an overlay token, sent as <c>Authorization: Bearer</c>.
/// Browsers cannot set headers on a WebSocket request, so the hub also accepts <c>access_token</c> in the
/// query string, and only the hub: an HTTP API that accepted keys in URLs would invite them into logs and history.
/// </summary>
public sealed class ChannelKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ChannelKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? key = null;
        var header = Request.Headers.Authorization.ToString();

        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            key = header["Bearer ".Length..].Trim();
        }
        else if (Request.Path.StartsWithSegments(OverlayHub.Path))
        {
            key = Request.Query["access_token"].ToString();
        }

        if (string.IsNullOrEmpty(key))
        {
            return AuthenticateResult.NoResult();
        }

        var hash = ChannelKeys.Hash(key);
        var match = await db.Channels.AsNoTracking()
            .Where(c => c.DashboardKeyHash == hash || c.OverlayTokenHash == hash)
            .Select(c => new { c.Id, IsDashboard = c.DashboardKeyHash == hash })
            .FirstOrDefaultAsync(Context.RequestAborted);

        if (match is null)
        {
            return AuthenticateResult.Fail("Unknown channel key.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ChannelClaims.ChannelId, match.Id.ToString()),
                new Claim(ClaimTypes.Role, match.IsDashboard ? ChannelRoles.Dashboard : ChannelRoles.Overlay),
            ],
            SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
