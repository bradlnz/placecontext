using System.Security.Cryptography;
using System.Text;
using PlaceContext.Identity.Auth;

namespace PlaceContext.Identity.Tests;

public sealed class PortalTokenTests
{
    [Fact]
    public void TryValidate_AcceptsValidTokenOnlyOnce()
    {
        var now = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        const string key = "a-test-key-that-is-long-enough-for-hmac-validation";
        var expires = now.AddMinutes(1).ToUnixTimeSeconds().ToString();
        const string nonce = "0123456789ABCDEF";
        var payload = $"{expires}.{nonce}";
        var signature = Convert.ToBase64String(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(key),
                Encoding.UTF8.GetBytes(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = $"{payload}.{signature}";
        var validator = new PortalToken();

        Assert.True(validator.TryValidate(token, key, now));
        Assert.False(validator.TryValidate(token, key, now));
    }
}
