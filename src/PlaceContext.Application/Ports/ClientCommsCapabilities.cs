namespace PlaceContext.Application.Ports;

public sealed record ClientCommsCapabilities(
    bool EmailEnabled,
    bool SmsEnabled,
    string EmailProvider,
    string SmsProvider);
