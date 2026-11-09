namespace PlaceContext.Application.Auth;

/// <summary>Org-wide 2FA requirement plus the user's own delivery preferences.</summary>
public sealed record TwoFactorSettingsInfo(
    bool Required,
    string PreferredChannel,
    string? PhoneNumber,
    bool EmailAvailable,
    bool SmsAvailable);
