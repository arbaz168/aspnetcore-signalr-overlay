using System.Security.Cryptography;
using System.Text;

namespace LiveOverlay.Api.Channels;

public static class ChannelKeys
{
    public const string DashboardPrefix = "dk_";
    public const string OverlayPrefix = "ot_";

    /// <summary>256 random bits, URL-safe, with a prefix so a leaked key is recognisable in a secret scan.</summary>
    public static string Generate(string prefix) =>
        prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Keys are high-entropy random values, so a plain SHA-256 is enough to make a database leak useless
    /// and lets the lookup use an index. Passwords would need a slow hash instead.
    /// </summary>
    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
