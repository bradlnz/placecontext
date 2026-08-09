using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PlaceContext.Communications.Contracts;

namespace PlaceContext.Communications.Infrastructure.Providers;

public sealed class CommunicationSender(
    IHttpClientFactory httpClientFactory,
    ICommunicationProviderService providers) : ICommunicationSender
{
    public async Task<CommunicationCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        var email = await providers.ResolveForSendAsync("email", ct);
        var sms = await providers.ResolveForSendAsync("sms", ct);
        return new CommunicationCapabilities(
            email is { SecretResolved: true },
            sms is { SecretResolved: true },
            email is null ? "Not configured" : Label(email.Kind),
            sms is null ? "Not configured" : Label(sms.Kind));
    }

    public async Task<CommunicationDelivery> SendEmailAsync(
        SendCommunicationEmailRequest request,
        CancellationToken ct = default)
    {
        var provider = request.Authentication
            ? await providers.ResolveForTwoFactorAsync("email", ct)
            : await providers.ResolveForSendAsync("email", ct);
        if (provider is null)
            throw new InvalidOperationException(
                "Email is not configured. Add an email provider in Settings → Communications.");
        return await SendEmailAsync(provider, request, request.Authentication ? "authentication" : "crm-transactional", ct);
    }

    public async Task<CommunicationDelivery> SendSmsAsync(
        SendCommunicationSmsRequest request,
        CancellationToken ct = default)
    {
        var provider = request.Authentication
            ? await providers.ResolveForTwoFactorAsync("sms", ct)
            : await providers.ResolveForSendAsync("sms", ct);
        if (provider is null)
            throw new InvalidOperationException(
                "SMS is not configured. Add an SMS provider in Settings → Communications.");
        return await SendTwilioAsync(provider, request.Recipient, request.Body, ct);
    }

    public async Task<CommunicationDelivery> SendTestAsync(
        Guid providerId,
        string recipient,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recipient)) throw new ArgumentException("Enter a recipient.");
        var provider = await providers.ResolveByIdAsync(providerId, ct);
        const string body = "This is a test message from your PlaceContext communications settings.";
        return provider.Channel == "email"
            ? await SendEmailAsync(provider, new SendCommunicationEmailRequest(
                recipient, string.Empty, "PlaceContext test message", body), "settings-test", ct)
            : await SendTwilioAsync(provider, recipient, body, ct);
    }

    private async Task<CommunicationDelivery> SendEmailAsync(
        ResolvedCommunicationProvider provider,
        SendCommunicationEmailRequest request,
        string tag,
        CancellationToken ct)
    {
        EnsureSecret(provider);
        return provider.Kind switch
        {
            "postmark" => await SendPostmarkAsync(provider, request, tag, ct),
            "sendgrid" => await SendGridAsync(provider, request, ct),
            _ => throw new InvalidOperationException($"Provider kind '{provider.Kind}' cannot send email."),
        };
    }

    private async Task<CommunicationDelivery> SendPostmarkAsync(
        ResolvedCommunicationProvider provider,
        SendCommunicationEmailRequest request,
        string tag,
        CancellationToken ct)
    {
        using var settings = Settings(provider);
        var endpoint = Value(settings, "endpoint") ?? "https://api.postmarkapp.com";
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/email");
        ApplyAuth(message, provider, null);
        message.Content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["From"] = Address(Value(settings, "fromEmail") ?? string.Empty, Value(settings, "fromName")),
            ["To"] = Address(request.Recipient, request.RecipientName),
            ["Subject"] = request.Subject,
            ["TextBody"] = request.Body,
            ["MessageStream"] = Value(settings, "messageStream") ?? "outbound",
            ["Tag"] = tag,
            ["Attachments"] = request.Attachments?.Select(attachment => new
            {
                attachment.Name,
                Content = attachment.ContentBase64,
                attachment.ContentType,
            }),
        });
        using var response = await httpClientFactory.CreateClient().SendAsync(message, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Postmark rejected the message ({(int)response.StatusCode}): {Trim(payload)}");
        using var json = JsonDocument.Parse(payload);
        if (json.RootElement.TryGetProperty("ErrorCode", out var code) && code.GetInt32() != 0)
            throw new InvalidOperationException("Postmark rejected the message: " + Text(json.RootElement, "Message"));
        return new CommunicationDelivery("Postmark", Text(json.RootElement, "MessageID"));
    }

    private async Task<CommunicationDelivery> SendGridAsync(
        ResolvedCommunicationProvider provider,
        SendCommunicationEmailRequest request,
        CancellationToken ct)
    {
        using var settings = Settings(provider);
        var endpoint = Value(settings, "endpoint") ?? "https://api.sendgrid.com/v3/mail/send";
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuth(message, provider, null);
        message.Content = JsonContent.Create(new Dictionary<string, object?>
        {
            ["personalizations"] = new[] { new { to = new[] { new { email = request.Recipient, name = request.RecipientName } } } },
            ["from"] = new { email = Value(settings, "fromEmail"), name = Value(settings, "fromName") },
            ["subject"] = request.Subject,
            ["content"] = new[] { new { type = "text/plain", value = request.Body } },
            ["attachments"] = request.Attachments?.Select(attachment => new
            {
                content = attachment.ContentBase64,
                type = attachment.ContentType,
                filename = attachment.Name,
                disposition = "attachment",
            }),
        });
        using var response = await httpClientFactory.CreateClient().SendAsync(message, ct);
        await EnsureDeliveryAsync("SendGrid", response, ct);
        var externalId = response.Headers.TryGetValues("X-Message-Id", out var values)
            ? values.FirstOrDefault()
            : null;
        return new CommunicationDelivery("SendGrid", externalId);
    }

    private async Task<CommunicationDelivery> SendTwilioAsync(
        ResolvedCommunicationProvider provider,
        string recipient,
        string body,
        CancellationToken ct)
    {
        EnsureSecret(provider);
        if (provider.Kind != "twilio")
            throw new InvalidOperationException($"Provider kind '{provider.Kind}' cannot send SMS.");
        using var settings = Settings(provider);
        var accountSid = Value(settings, "accountSid") ?? string.Empty;
        var endpoint = (Value(settings, "endpoint") ?? "https://api.twilio.com").TrimEnd('/')
            + $"/2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages.json";
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        ApplyAuth(message, provider, accountSid);
        message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["To"] = recipient,
            ["From"] = Value(settings, "fromNumber") ?? string.Empty,
            ["Body"] = body,
        });
        using var response = await httpClientFactory.CreateClient().SendAsync(message, ct);
        await EnsureDeliveryAsync("Twilio", response, ct);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return new CommunicationDelivery("Twilio", Text(json.RootElement, "sid"));
    }

    private static void ApplyAuth(
        HttpRequestMessage request,
        ResolvedCommunicationProvider provider,
        string? basicUsername)
    {
        if (provider.AuthType == "bearer")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.Secret);
        else if (provider.AuthType == "header")
            request.Headers.TryAddWithoutValidation(provider.AuthHeaderName ?? "X-Api-Key", provider.Secret);
        else if (provider.AuthType == "basic")
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{basicUsername}:{provider.Secret}")));
    }

    private static void EnsureSecret(ResolvedCommunicationProvider provider)
    {
        if (provider.RequiresSecret && !provider.SecretResolved)
            throw new InvalidOperationException($"The Vault secret referenced by provider '{provider.Name}' is unavailable.");
    }

    private static JsonDocument Settings(ResolvedCommunicationProvider provider)
    {
        try { return JsonDocument.Parse(provider.SettingsJson); }
        catch (JsonException) { return JsonDocument.Parse("{}"); }
    }

    private static string? Value(JsonDocument settings, string name)
        => settings.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static string? Text(JsonElement value, string name)
        => value.TryGetProperty(name, out var property) ? property.GetString() : null;

    private static async Task EnsureDeliveryAsync(
        string provider,
        HttpResponseMessage response,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = Trim(await response.Content.ReadAsStringAsync(ct));
        throw new InvalidOperationException($"{provider} rejected the message ({(int)response.StatusCode}): {detail}");
    }

    private static string Address(string email, string? name)
        => string.IsNullOrWhiteSpace(name)
            ? email
            : $"\"{name.Replace("\"", "\\\"")}\" <{email}>";

    private static string Label(string kind) => kind switch
    {
        "postmark" => "Postmark",
        "sendgrid" => "SendGrid",
        "twilio" => "Twilio",
        _ => kind,
    };

    private static string Trim(string value) => value.Length > 500 ? value[..500] : value;
}
