using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Crm.Integration;

namespace PlaceContext.Crm.Infrastructure.Integration;

public sealed class HttpCrmCommunicationsClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : ICrmCommunicationsClient
{
    public async Task<CrmCommunicationCapabilities> GetCapabilitiesAsync(
        CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "api/communications/internal/capabilities");
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CrmCommunicationCapabilities>(ct)
            ?? new CrmCommunicationCapabilities(false, false, "Not configured", "Not configured");
    }

    public async Task<CrmMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default,
        IReadOnlyList<CrmEmailAttachment>? attachments = null)
        => await SendAsync(
            "api/communications/internal/email",
            new { recipient, recipientName, subject, body, attachments },
            ct);

    public async Task<CrmMessageDelivery> SendSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default)
        => await SendAsync(
            "api/communications/internal/sms",
            new { recipient, body },
            ct);

    private async Task<CrmMessageDelivery> SendAsync(
        string path,
        object payload,
        CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = JsonContent.Create(payload);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CrmMessageDelivery>(ct)
            ?? throw new InvalidOperationException("The Communications service returned an empty response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        var origin = configuration["PlaceContext:Crm:Communications:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Communications"]
            ?? throw new InvalidOperationException("Configure the Communications service destination for CRM.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        var request = new HttpRequestMessage(method, new Uri(new Uri(baseAddress), relativePath));
        request.Headers.Add("X-Api-Key", apiKey);
        return request;
    }
}
