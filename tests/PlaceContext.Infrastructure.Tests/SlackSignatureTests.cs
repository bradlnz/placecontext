using System.Security.Cryptography;
using System.Text;
using PlaceContext.Infrastructure.Slack;

namespace PlaceContext.Infrastructure.Tests;

public sealed class SlackSignatureTests
{
    [Fact]
    public void Accepts_valid_v0_signature()
    {
        const string secret = "test_secret";
        const string body = """{"type":"url_verification","challenge":"abc"}""";
        var now = DateTimeOffset.Parse("2026-07-28T12:00:00Z");
        var ts = now.ToUnixTimeSeconds().ToString();
        var sig = Sign(secret, ts, body);

        Assert.True(SlackSignature.IsValid(secret, ts, sig, Encoding.UTF8.GetBytes(body), now));
    }

    [Fact]
    public void Rejects_wrong_secret()
    {
        const string body = "{}";
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds().ToString();
        var sig = Sign("right", ts, body);

        Assert.False(SlackSignature.IsValid("wrong", ts, sig, Encoding.UTF8.GetBytes(body), now));
    }

    [Fact]
    public void Rejects_stale_timestamp()
    {
        const string secret = "s";
        const string body = "{}";
        var now = DateTimeOffset.UtcNow;
        var stale = now.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var sig = Sign(secret, stale, body);

        Assert.False(SlackSignature.IsValid(secret, stale, sig, Encoding.UTF8.GetBytes(body), now));
    }

    private static string Sign(string secret, string ts, string body)
    {
        var bas = $"v0:{ts}:{body}";
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(bas));
        return "v0=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
