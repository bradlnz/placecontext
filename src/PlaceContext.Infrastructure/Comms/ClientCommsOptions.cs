namespace PlaceContext.Infrastructure.Comms;

public sealed class ClientCommsOptions
{
    public const string SectionName = "PlaceContext:Comms";
    public EmailCommsOptions Email { get; set; } = new();
    public PostmarkOptions Postmark { get; set; } = new();
    public SmsCommsOptions Sms { get; set; } = new();
}

public sealed class EmailCommsOptions
{
    public string ApiKey { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "PlaceContext";
    public string Endpoint { get; set; } = "https://api.sendgrid.com/v3/mail/send";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromEmail);
}

public sealed class PostmarkOptions
{
    public string ApiEndpoint { get; set; } = "https://api.postmarkapp.com";
}

public sealed class SmsCommsOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string FromNumber { get; set; } = "";
    public string Endpoint { get; set; } = "https://api.twilio.com";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AccountSid)
        && !string.IsNullOrWhiteSpace(AuthToken)
        && !string.IsNullOrWhiteSpace(FromNumber);
}
