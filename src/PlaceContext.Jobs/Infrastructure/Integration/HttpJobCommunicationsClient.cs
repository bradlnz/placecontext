using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Jobs.Integration;

namespace PlaceContext.Jobs.Infrastructure.Integration;

public sealed class HttpJobCommunicationsClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IJobCommunicationsClient
{
    public string EmailProvider => "Communications Email";
    public string SmsProvider => "Communications SMS";

    public Task<JobMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default,
        IReadOnlyList<JobEmailAttachment>? attachments = null)
        => SendAsync(
            "api/communications/internal/email",
            new { recipient, recipientName, subject, body, attachments },
            ct);

    public Task<JobMessageDelivery> SendSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default)
        => SendAsync("api/communications/internal/sms", new { recipient, body }, ct);

    private async Task<JobMessageDelivery> SendAsync(
        string path,
        object payload,
        CancellationToken ct)
    {
        var origin = configuration["PlaceContext:Jobs:Communications:BaseAddress"]
            ?? configuration["PlaceContext:Microservices:Destinations:Communications"]
            ?? throw new InvalidOperationException(
                "Configure the Communications service destination for Jobs.");
        var apiKey = configuration["PlaceContext:Api:Key"]
            ?? throw new InvalidOperationException("Configure PlaceContext:Api:Key for service calls.");
        var baseAddress = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(baseAddress), path));
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = JsonContent.Create(payload);
        using var response = await httpClientFactory.CreateClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JobMessageDelivery>(ct)
            ?? throw new InvalidOperationException(
                "The Communications service returned an empty response.");
    }
}
