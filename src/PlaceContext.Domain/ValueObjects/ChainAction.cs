using System.Net.Mail;

namespace PlaceContext.Domain.ValueObjects;

/// <summary>A typed non-job operation that can occupy one chain stage.</summary>
public abstract class ChainAction
{
    public abstract string Type { get; }
    public abstract string DisplayName { get; }
}

/// <summary>
/// Sends one plain-text transactional email through the tenant's configured communication provider.
/// Double-brace payload paths (for example <c>{{report.client.name}}</c>) are resolved at run time.
/// </summary>
public sealed class SendEmailChainAction : ChainAction
{
    public const string ActionType = "sendEmail";

    public SendEmailChainAction(
        string recipient,
        string recipientName,
        string subject,
        string body,
        string attachmentPath = "")
    {
        Recipient = Required(recipient, nameof(recipient), 320);
        RecipientName = Optional(recipientName, 200);
        Subject = Required(subject, nameof(subject), 500);
        Body = Required(body, nameof(body), 100_000);
        AttachmentPath = Optional(attachmentPath, 500, nameof(attachmentPath));

        // A templated recipient is validated after substitution; literals must already be valid.
        if (!ContainsTemplate(Recipient))
        {
            try { _ = new MailAddress(Recipient); }
            catch (FormatException) { throw new ArgumentException("Enter a valid email recipient.", nameof(recipient)); }
        }
    }

    public override string Type => ActionType;
    public override string DisplayName => "Send email";
    public string Recipient { get; }
    public string RecipientName { get; }
    public string Subject { get; }
    public string Body { get; }
    /// <summary>
    /// Optional path into the previous stage's JSON payload. The resolved value may be one
    /// attachment object or an array of objects containing a name, content, and content type.
    /// </summary>
    public string AttachmentPath { get; }

    private static bool ContainsTemplate(string value)
        => value.Contains("{{", StringComparison.Ordinal)
           && value.Contains("}}", StringComparison.Ordinal);

    private static string Required(string value, string parameter, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameter} is required.", parameter);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"{parameter} is too long.", parameter);
        return trimmed;
    }

    private static string Optional(string? value, int maxLength, string parameter = "recipientName")
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"{parameter} is too long.", parameter);
        return trimmed;
    }
}

/// <summary>Sends one SMS through the tenant's configured SMS provider.</summary>
public sealed class SendSmsChainAction : ChainAction
{
    public const string ActionType = "sendSms";

    public SendSmsChainAction(string recipient, string body)
    {
        Recipient = Required(recipient, nameof(recipient), 80);
        Body = Required(body, nameof(body), 1_600);
        if (!ContainsTemplate(Recipient))
        {
            var normalized = new string(Recipient.Where(char.IsDigit).ToArray());
            if (!Recipient.StartsWith('+') || normalized.Length is < 8 or > 15)
                throw new ArgumentException(
                    "Enter an international SMS recipient such as +61412345678.", nameof(recipient));
        }
    }

    public override string Type => ActionType;
    public override string DisplayName => "Send SMS";
    public string Recipient { get; }
    public string Body { get; }

    private static bool ContainsTemplate(string value)
        => value.Contains("{{", StringComparison.Ordinal)
           && value.Contains("}}", StringComparison.Ordinal);

    private static string Required(string value, string parameter, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameter} is required.", parameter);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"{parameter} is too long.", parameter);
        return trimmed;
    }
}
