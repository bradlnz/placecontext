using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Comms;

public sealed class SendGridTwilioCommunicationSender : IClientCommunicationSender
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ClientCommsOptions _options;

    public SendGridTwilioCommunicationSender(
        IHttpClientFactory httpFactory,
        IOptions<ClientCommsOptions> options)
        => (_httpFactory, _options) = (httpFactory, options.Value);

    public ClientCommsCapabilities Capabilities => new(
        _options.Email.IsConfigured,
        _options.Sms.IsConfigured,
        "SendGrid",
        "Twilio");

    public async Task<ClientMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        var options = _options.Email;
        if (!options.IsConfigured)
            throw new InvalidOperationException("Email is not configured. Add the SendGrid API key and sender address.");

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            personalizations = new[] { new { to = new[] { new { email = recipient, name = recipientName } } } },
            from = new { email = options.FromEmail, name = options.FromName },
            subject,
            content = new[] { new { type = "text/plain", value = body } },
        });
        using var response = await _httpFactory.CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await DeliveryError("SendGrid", response, ct));
        var externalId = response.Headers.TryGetValues("X-Message-Id", out var values)
            ? values.FirstOrDefault()
            : null;
        return new ClientMessageDelivery("SendGrid", externalId);
    }

    public async Task<ClientMessageDelivery> SendSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default)
    {
        var options = _options.Sms;
        if (!options.IsConfigured)
            throw new InvalidOperationException("SMS is not configured. Add the Twilio account, token, and sender number.");

        var endpoint = $"{options.Endpoint.TrimEnd('/')}/2010-04-01/Accounts/{Uri.EscapeDataString(options.AccountSid)}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = recipient,
            ["From"] = options.FromNumber,
            ["Body"] = body,
        });
        using var response = await _httpFactory.CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await DeliveryError("Twilio", response, ct));
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var externalId = json.RootElement.TryGetProperty("sid", out var sid) ? sid.GetString() : null;
        return new ClientMessageDelivery("Twilio", externalId);
    }

    private static async Task<string> DeliveryError(
        string provider,
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var detail = await response.Content.ReadAsStringAsync(ct);
        if (detail.Length > 400) detail = detail[..400];
        return $"{provider} rejected the message ({(int)response.StatusCode}): {detail}";
    }
}
