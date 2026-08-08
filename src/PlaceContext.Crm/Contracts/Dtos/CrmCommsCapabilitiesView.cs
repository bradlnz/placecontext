namespace PlaceContext.Application.Features;

public sealed record CrmCommsCapabilitiesView(
    bool EmailEnabled,
    bool SmsEnabled,
    string EmailProvider,
    string SmsProvider);
