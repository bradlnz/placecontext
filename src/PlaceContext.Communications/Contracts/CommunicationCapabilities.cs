namespace PlaceContext.Communications.Contracts;

public sealed record CommunicationCapabilities(
    bool EmailEnabled,
    bool SmsEnabled,
    string EmailProvider,
    string SmsProvider);
