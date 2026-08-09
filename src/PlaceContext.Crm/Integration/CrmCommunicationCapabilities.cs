namespace PlaceContext.Crm.Integration;

public sealed record CrmCommunicationCapabilities(
    bool EmailEnabled,
    bool SmsEnabled,
    string EmailProvider,
    string SmsProvider);
