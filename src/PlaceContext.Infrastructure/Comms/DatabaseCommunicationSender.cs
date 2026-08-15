using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Comms;

/// <summary>
/// Sends client email/SMS through the tenant's configured communication providers
/// (<see cref="CommunicationProviderService"/>). Payload shapes are built per provider kind
/// (Postmark JSON, SendGrid JSON, Twilio form-encoded); the auth mechanism (header/bearer/basic)
/// and endpoint come from the provider row, and secrets are decrypted from Vault at send time.
/// </summary>
public sealed class DatabaseCommunicationSender : IClientCommunicationSender
{
    private const string SendGridDefaultEndpoint = "https://api.sendgrid.com/v3/mail/send";
    private const string TwilioDefaultEndpoint = "https://api.twilio.com";

    private static readonly JsonSerializerOptions PostmarkJson = new()
    {
        PropertyNamingPolicy = null,
    };
    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly ClientCommsOptions _options;
    private readonly CommunicationProviderService _providers;

    public DatabaseCommunicationSender(
        IHttpClientFactory httpFactory,
        IOptions<ClientCommsOptions> options,
        CommunicationProviderService providers)
        => (_httpFactory, _options, _providers) = (httpFactory, options.Value, providers);

    public string EmailProvider => "Email";
    public string SmsProvider => "SMS";

    public async Task<ClientCommsCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var email = await _providers.ResolveForSendAsync("email", ct);
        var sms = await _providers.ResolveForSendAsync("sms", ct);
        return new ClientCommsCapabilities(
            email is { SecretResolved: true },
            sms is { SecretResolved: true },
            email is null ? "Not configured" : ProviderLabel(email.Kind),
            sms is null ? "Not configured" : ProviderLabel(sms.Kind));
    }

    public async Task<ClientMessageDelivery> SendEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default,
        IReadOnlyList<ClientEmailAttachment>? attachments = null)
    {
        var provider = await _providers.ResolveForSendAsync("email", ct)
            ?? throw new InvalidOperationException(
                "Email is not configured. Add an email provider in Settings → Communications.");
        return await SendResolvedEmailAsync(
            provider, recipient, recipientName, subject, body, "job-transactional", ct, attachments);
    }

    public async Task<ClientMessageDelivery> SendAuthenticationEmailAsync(
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        var provider = await _providers.ResolveForTwoFactorAsync("email", ct)
            ?? throw new InvalidOperationException(
                "Authentication email requires an email provider. Add one in Settings → Communications.");
        return await SendResolvedEmailAsync(
            provider, recipient, recipientName, subject, body, "authentication", ct);
    }

    public async Task<ClientMessageDelivery> SendSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default)
    {
        var provider = await _providers.ResolveForSendAsync("sms", ct)
            ?? throw new InvalidOperationException(
                "SMS is not configured. Add an SMS provider in Settings → Communications.");
        return await SendTwilioSmsAsync(provider, recipient, body, ct);
    }

    public async Task<ClientMessageDelivery> SendAuthenticationSmsAsync(
        string recipient,
        string body,
        CancellationToken ct = default)
    {
        var provider = await _providers.ResolveForTwoFactorAsync("sms", ct)
            ?? throw new InvalidOperationException(
                "Authentication SMS requires an SMS provider. Add one in Settings → Communications.");
        return await SendTwilioSmsAsync(provider, recipient, body, ct);
    }

    /// <summary>
    /// Sends a short test message through a specific provider (not necessarily the channel
    /// default), used by the settings page to verify a configuration end to end.
    /// </summary>
    public async Task<ClientMessageDelivery> SendTestAsync(
        Guid providerId,
        string recipient,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("Enter a recipient for the test message.");
        var provider = await _providers.ResolveByIdAsync(providerId, ct);
        const string body = "This is a test message from your PlaceContext communications settings.";
        return provider.Channel switch
        {
            "email" => await SendResolvedEmailAsync(
                provider, recipient, "", "PlaceContext test message", body, "settings-test", ct),
            "sms" => await SendTwilioSmsAsync(provider, recipient, body, ct),
            _ => throw new InvalidOperationException(
                $"Provider channel '{provider.Channel}' cannot send test messages."),
        };
    }

    private Task<ClientMessageDelivery> SendResolvedEmailAsync(
        ResolvedProvider provider,
        string recipient,
        string recipientName,
        string subject,
        string body,
        string tag,
        CancellationToken ct,
        IReadOnlyList<ClientEmailAttachment>? attachments = null)
    {
        EnsureSecretResolved(provider);
        return provider.Kind switch
        {
            "postmark" => SendPostmarkEmailAsync(
                provider, recipient, recipientName, subject, body, tag, ct, attachments),
            "sendgrid" => SendSendGridEmailAsync(
                provider, recipient, recipientName, subject, body, ct, attachments),
            _ => throw new InvalidOperationException(
                $"Provider kind '{provider.Kind}' cannot send email."),
        };
    }

    private async Task<ClientMessageDelivery> SendPostmarkEmailAsync(
        ResolvedProvider provider,
        string recipient,
        string recipientName,
        string subject,
        string body,
        string tag,
        CancellationToken ct,
        IReadOnlyList<ClientEmailAttachment>? attachments = null)
    {
        var settings = Settings<PostmarkSettings>(provider);
        var endpointBase = string.IsNullOrWhiteSpace(settings.Endpoint)
            ? _options.Postmark.ApiEndpoint
            : settings.Endpoint!;
        var endpoint = $"{endpointBase.TrimEnd('/')}/email";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuth(request, provider, basicUsername: null);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var postmarkPayload = new Dictionary<string, object?>
        {
            ["From"] = Address(settings.FromEmail, settings.FromName),
            ["To"] = Address(recipient, recipientName),
            ["Subject"] = subject,
            ["TextBody"] = body,
            ["MessageStream"] = string.IsNullOrWhiteSpace(settings.MessageStream)
                ? "outbound"
                : settings.MessageStream,
            ["Tag"] = tag,
        };
        if (attachments is { Count: > 0 })
        {
            postmarkPayload["Attachments"] = attachments.Select(a => new
            {
                Name = a.Name,
                Content = a.ContentBase64,
                ContentType = a.ContentType,
            }).ToArray();
        }
        request.Content = JsonContent.Create(postmarkPayload, options: PostmarkJson);
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

    private async Task<ClientMessageDelivery> SendSendGridEmailAsync(
        ResolvedProvider provider,
        string recipient,
        string recipientName,
        string subject,
        string body,
        CancellationToken ct,
        IReadOnlyList<ClientEmailAttachment>? attachments = null)
    {
        var settings = Settings<SendGridSettings>(provider);
        var endpoint = string.IsNullOrWhiteSpace(settings.Endpoint)
            ? SendGridDefaultEndpoint
            : settings.Endpoint!;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuth(request, provider, basicUsername: null);
        var sendGridPayload = new Dictionary<string, object?>
        {
            ["personalizations"] = new[] { new { to = new[] { new { email = recipient, name = recipientName } } } },
            ["from"] = new { email = settings.FromEmail, name = settings.FromName },
            ["subject"] = subject,
            ["content"] = new[] { new { type = "text/plain", value = body } },
        };
        if (attachments is { Count: > 0 })
        {
            sendGridPayload["attachments"] = attachments.Select(a => new
            {
                content = a.ContentBase64,
                type = a.ContentType,
                filename = a.Name,
                disposition = "attachment",
            }).ToArray();
        }
        request.Content = JsonContent.Create(sendGridPayload);
        using var response = await _httpFactory.CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await DeliveryError("SendGrid", response, ct));
        var externalId = response.Headers.TryGetValues("X-Message-Id", out var values)
            ? values.FirstOrDefault()
            : null;
        return new ClientMessageDelivery("SendGrid", externalId);
    }

    private async Task<ClientMessageDelivery> SendTwilioSmsAsync(
        ResolvedProvider provider,
        string recipient,
        string body,
        CancellationToken ct)
    {
        EnsureSecretResolved(provider);
        if (provider.Kind != "twilio")
            throw new InvalidOperationException(
                $"Provider kind '{provider.Kind}' cannot send SMS.");
        var settings = Settings<TwilioSettings>(provider);
        var endpointBase = string.IsNullOrWhiteSpace(settings.Endpoint)
            ? TwilioDefaultEndpoint
            : settings.Endpoint!;
        var endpoint = $"{endpointBase.TrimEnd('/')}/2010-04-01/Accounts/{Uri.EscapeDataString(settings.AccountSid)}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuth(request, provider, settings.AccountSid);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = recipient,
            ["From"] = settings.FromNumber,
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

    /// <summary>Applies the provider's configured auth mechanism to the outgoing request.</summary>
    private static void ApplyAuth(HttpRequestMessage request, ResolvedProvider provider, string? basicUsername)
    {
        switch (provider.AuthType)
        {
            case "bearer":
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.Secret);
                break;
            case "header":
                request.Headers.TryAddWithoutValidation(
                    provider.AuthHeaderName ?? "X-Api-Key", provider.Secret);
                break;
            case "basic":
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{basicUsername}:{provider.Secret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                break;
        }
    }

    private static void EnsureSecretResolved(ResolvedProvider provider)
    {
        if (provider.RequiresSecret && !provider.SecretResolved)
            throw new InvalidOperationException(
                $"The Vault secret referenced by provider '{provider.Name}' is no longer available.");
    }

    private static T Settings<T>(ResolvedProvider provider) where T : new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(provider.SettingsJson, SettingsJson) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }

    private static string ProviderLabel(string kind) => kind switch
    {
        "postmark" => "Postmark",
        "sendgrid" => "SendGrid",
        "twilio" => "Twilio",
        _ => kind,
    };

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

    private sealed class PostmarkSettings
    {
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "";
        public string MessageStream { get; set; } = "";
        public string? Endpoint { get; set; }
    }

    private sealed class SendGridSettings
    {
        public string FromEmail { get; set; } = "";
        public string FromName { get; set; } = "";
        public string? Endpoint { get; set; }
    }

    private sealed class TwilioSettings
    {
        public string AccountSid { get; set; } = "";
        public string FromNumber { get; set; } = "";
        public string? Endpoint { get; set; }
    }
}
