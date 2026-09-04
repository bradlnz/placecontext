using System.Net.Mail;

namespace PlaceContext.Domain.ValueObjects;

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
