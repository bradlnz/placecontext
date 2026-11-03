namespace PlaceContext.Application.Communications;

public sealed record ClientCommsCapabilities(
    bool EmailEnabled,
    bool SmsEnabled,
    string EmailProvider,
    string SmsProvider);
