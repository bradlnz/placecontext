using System.Net;
using System.Net.Http.Json;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Activation;
using PlaceContext.Licensing.Models;
using PlaceContext.Licensing.Signing;
using PlaceContext.TestSupport;
using Microsoft.Extensions.Configuration;

namespace PlaceContext.Infrastructure.Tests;

/// <summary>
/// Tests the online (phone-home) activation client end-to-end against the real licensor signer: it activates
/// from the server, follows signing-key rotation through the endorsement chain, holds the line within the
/// grace window when the server is unreachable, and honours an explicit payment/denial response immediately.
/// </summary>
public class RemoteActivationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);
    private const string ServerUrl = "http://licensing.test";
    private const string LicenseKey = "DEMO-PLACECONTEXT";

    private static RemoteActivationService Build(
        ActivationSigner signer, StubHandler handler, FakeClock clock, double graceDays = 7)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlaceContext:Activation:ServerUrl"] = ServerUrl,
            ["PlaceContext:Activation:Key"] = LicenseKey,
            ["PlaceContext:Activation:PublicKey"] = signer.RootPublicKeyBase64,
            ["PlaceContext:Activation:GraceDays"] = graceDays.ToString(),
            ["PlaceContext:Activation:Enforce"] = "true",
        }).Build();
        return new RemoteActivationService(new StubHttpClientFactory(handler), config, clock);
    }

    private static HttpResponseMessage Ok(ActivationSigner signer, string? plan = "pro")
    {
        var token = signer.SignToken("Acme Inc", plan, Now.AddDays(30));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ActivateResponse(token, signer.KeySet()))
        };
    }

    [Fact]
    public async Task Activates_from_server_and_reads_claims()
    {
        using var signer = new ActivationSigner();
        var handler = new StubHandler(_ => Ok(signer));
        var svc = Build(signer, handler, new FakeClock(Now));

        await svc.RefreshAsync();

        Assert.True(svc.Current.IsActive);
        Assert.Equal("Acme Inc", svc.Current.Organisation);
        Assert.Equal("pro", svc.Current.Plan);
    }

    [Fact]
    public async Task Trusts_a_rotated_in_key_via_the_endorsement_chain()
    {
        using var signer = new ActivationSigner();
        // The server rotates its signing key; tokens are now signed by the new key, endorsed by the root.
        signer.Rotate();
        var handler = new StubHandler(_ => Ok(signer));
        var svc = Build(signer, handler, new FakeClock(Now));

        await svc.RefreshAsync();

        // The client is anchored only on the ROOT key, yet the rotated token verifies through the chain.
        Assert.True(svc.Current.IsActive);
    }

    [Fact]
    public async Task Rejects_a_token_from_a_key_not_chained_to_the_anchor()
    {
        using var anchorSigner = new ActivationSigner();   // the client's trust anchor
        using var rogueSigner = new ActivationSigner();    // an unrelated keypair
        // Server response is signed by the rogue key and advertises only the rogue key set.
        var handler = new StubHandler(_ => Ok(rogueSigner));
        var svc = Build(anchorSigner, handler, new FakeClock(Now));

        await svc.RefreshAsync();

        Assert.False(svc.Current.IsActive);
        Assert.Equal(ActivationStatus.Invalid, svc.Current.Status);
    }

    [Fact]
    public async Task Stays_active_within_grace_when_server_becomes_unreachable()
    {
        using var signer = new ActivationSigner();
        var clock = new FakeClock(Now);
        var handler = new StubHandler(_ => Ok(signer));
        var svc = Build(signer, handler, clock, graceDays: 7);

        await svc.RefreshAsync();            // good activation cached
        Assert.True(svc.Current.IsActive);

        handler.Responder = _ => throw new HttpRequestException("connection refused");

        clock.UtcNow = Now.AddDays(3);       // within the 7-day grace window
        await svc.RefreshAsync();
        Assert.True(svc.Current.IsActive);
        Assert.Contains("grace", svc.Current.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Degrades_after_the_grace_window_elapses()
    {
        using var signer = new ActivationSigner();
        var clock = new FakeClock(Now);
        var handler = new StubHandler(_ => Ok(signer));
        var svc = Build(signer, handler, clock, graceDays: 7);

        await svc.RefreshAsync();
        handler.Responder = _ => throw new HttpRequestException("connection refused");

        clock.UtcNow = Now.AddDays(10);      // past the grace window
        await svc.RefreshAsync();

        Assert.False(svc.Current.IsActive);
        Assert.Equal(ActivationStatus.Expired, svc.Current.Status);
    }

    [Fact]
    public async Task Payment_required_is_honoured_immediately_without_grace()
    {
        using var signer = new ActivationSigner();
        var clock = new FakeClock(Now);
        var handler = new StubHandler(_ => Ok(signer));
        var svc = Build(signer, handler, clock);

        await svc.RefreshAsync();            // active first
        Assert.True(svc.Current.IsActive);

        handler.Responder = _ => new HttpResponseMessage(HttpStatusCode.PaymentRequired)
        {
            Content = new StringContent("Subscription payment is past due.")
        };
        await svc.RefreshAsync();

        Assert.False(svc.Current.IsActive);  // server said no — grace does not apply
        Assert.Equal(ActivationStatus.Invalid, svc.Current.Status);
        Assert.Contains("past due", svc.Current.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => Responder = responder;
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Responder(request));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
