using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;

namespace PlaceContext.Identity.Infrastructure.Communications;

public sealed class HttpIdentityCommunicationSender(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IClientCommunicationSender
{
    public string EmailProvider => "Communications Email";
    public string SmsProvider => "Communications SMS";

    public Task<IReadOnlyList<string>> TwoFactorChannelsAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<string>>("api/communications/internal/two-factor-channels", ct);

    public Task<ClientCommsCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
        => GetAsync<ClientCommsCapabilities>("api/communications/internal/capabilities", ct);

    public Task<ClientMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default,
        IReadOnlyList<ClientEmailAttachment>? attachments = null)
        => SendAsync(
            "api/communications/internal/email",
            new { recipient, recipientName, subject, body, attachments },
            ct);

    public Task<ClientMessageDelivery> SendAuthenticationEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default)
        => SendAsync(
            "api/communications/internal/email",
            new { recipient, recipientName, subject, body, authentication = true },
            ct);

    public Task<ClientMessageDelivery> SendSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default)
        => SendAsync("api/communications/internal/sms", new { recipient, body }, ct);

    public Task<ClientMessageDelivery> SendAuthenticationSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default)
        => SendAsync(
            "api/communications/internal/sms",
            new { recipient, body, authentication = true },
            ct);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredJsonAsync<T>(response.Content, ct);
    }

    private async Task<ClientMessageDelivery> SendAsync(
        string path,
        object payload,
        CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(payload);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredJsonAsync<ClientMessageDelivery>(response.Content, ct);
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(
        HttpContent content,
        CancellationToken ct)
    {
        var body = await content.ReadAsByteArrayAsync(ct);
        if (body.Length == 0)
            throw new InvalidOperationException(
                "The Communications service returned an empty response.");

        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException(
                "The Communications service returned an empty response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var origin = configuration["PlaceContext:Identity:Communications:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Communications"]
            ?? throw new InvalidOperationException(
                "Configure the Communications service destination for Identity.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), path));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
