using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using PlaceContext.Identity.Infrastructure.Communications;

namespace PlaceContext.Identity.Tests;

public sealed class HttpIdentityCommunicationSenderTests
{
    [Fact]
    public async Task TwoFactorChannelsAsync_uses_internal_endpoint_and_api_key()
    {
        HttpRequestMessage? capturedRequest = null;
        var sender = CreateSender(request =>
        {
            capturedRequest = request;
            return JsonResponse("[\"email\",\"sms\"]");
        });

        var channels = await sender.TwoFactorChannelsAsync();

        Assert.Equal(["email", "sms"], channels);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest.Method);
        Assert.Equal(
            "https://communications.test/api/communications/internal/two-factor-channels",
            capturedRequest.RequestUri?.AbsoluteUri);
        Assert.Equal("service-key", Assert.Single(capturedRequest.Headers.GetValues("X-Api-Key")));
    }

    [Fact]
    public async Task SendAuthenticationEmailAsync_marks_the_delivery_as_authentication()
    {
        string? payload = null;
        var sender = CreateSender(async request =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            return JsonResponse(
                "{\"provider\":\"Postmark\",\"externalId\":\"message-123\"}");
        });

        var result = await sender.SendAuthenticationEmailAsync(
            "person@example.test",
            "Person",
            "Verification code",
            "123456");

        Assert.Equal("Postmark", result.Provider);
        Assert.Equal("message-123", result.ExternalId);
        Assert.Contains("\"authentication\":true", payload, StringComparison.Ordinal);
        Assert.Contains("\"recipient\":\"person@example.test\"", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoFactorChannelsAsync_rejects_an_empty_success_response()
    {
        var sender = CreateSender(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.TwoFactorChannelsAsync());

        Assert.Equal("The Communications service returned an empty response.", error.Message);
    }

    private static HttpIdentityCommunicationSender CreateSender(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
        => CreateSender(request => Task.FromResult(respond(request)));

    private static HttpIdentityCommunicationSender CreateSender(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlaceContext:Identity:Communications:BaseAddress"] =
                    "https://communications.test",
                ["PlaceContext:Api:Key"] = "service-key"
            })
            .Build();
        return new HttpIdentityCommunicationSender(
            new StubHttpClientFactory(new StubHandler(respond)),
            configuration);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
