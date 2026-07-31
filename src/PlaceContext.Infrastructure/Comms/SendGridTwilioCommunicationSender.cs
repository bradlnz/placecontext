using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Comms;

public sealed class SendGridTwilioCommunicationSender : IClientCommunicationSender
{
    private static readonly JsonSerializerOptions PostmarkJson = new()
    {
        PropertyNamingPolicy = null,
    };
    private readonly IHttpClientFactory _httpFactory;
    private readonly ClientCommsOptions _options;
    private readonly PostmarkConnectionService _postmark;

    public SendGridTwilioCommunicationSender(
        IHttpClientFactory httpFactory,
        IOptions<ClientCommsOptions> options,
        PostmarkConnectionService postmark)
        => (_httpFactory, _options, _postmark) = (httpFactory, options.Value, postmark);

    public string EmailProvider => "Postmark";
    public string SmsProvider => "Twilio";

    public async Task<ClientCommsCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var postmark = await _postmark.GetStatusAsync(ct);
        return new ClientCommsCapabilities(
            postmark.Ready || _options.Email.IsConfigured,
            _options.Sms.IsConfigured,
            postmark.Configured || !_options.Email.IsConfigured ? "Postmark" : "SendGrid",
            "Twilio");
    }

    public async Task<ClientMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        var postmark = await _postmark.GetSendingCredentialAsync(ct);
        if (postmark is not null)
            return await SendPostmarkEmailAsync(
                postmark, recipient, recipientName, subject, body, ct);

        var options = _options.Email;
        if (!options.IsConfigured)
            throw new InvalidOperationException(
                "Email is not configured. Connect Postmark in Settings → Communications.");

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

    private async Task<ClientMessageDelivery> SendPostmarkEmailAsync(
        PostmarkSendingCredential credential,
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct)
    {
        var endpoint = $"{_options.Postmark.ApiEndpoint.TrimEnd('/')}/email";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.TryAddWithoutValidation("X-Postmark-Server-Token", credential.ServerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            From = Address(credential.FromEmail, credential.FromName),
            To = Address(recipient, recipientName),
            Subject = subject,
            TextBody = body,
            MessageStream = credential.MessageStream,
            Tag = "crm-transactional",
        }, options: PostmarkJson);
        using var response = await _httpFactory.CreateClient().SendAsync(request, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Postmark rejected the message ({(int)response.StatusCode}): {Trim(payload)}");

        using var json = JsonDocument.Parse(payload);
        var errorCode = json.RootElement.TryGetProperty("ErrorCode", out var error)
            ? error.GetInt32()
            : 0;
        if (errorCode != 0)
        {
            var detail = json.RootElement.TryGetProperty("Message", out var message)
                ? message.GetString()
                : payload;
            throw new InvalidOperationException($"Postmark rejected the message ({errorCode}): {detail}");
        }
        var externalId = json.RootElement.TryGetProperty("MessageID", out var messageId)
            ? messageId.GetString()
            : null;
        return new ClientMessageDelivery("Postmark", externalId);
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

    private static string Address(string email, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return email;
        var safeName = name.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{safeName}\" <{email}>";
    }

    private static string Trim(string value) => value.Length > 500 ? value[..500] : value;
}
