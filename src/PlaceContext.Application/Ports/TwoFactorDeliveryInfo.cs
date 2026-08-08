using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>Delivery routing for the login verify page (see <see cref="IAuthService.GetTwoFactorDeliveryInfoAsync"/>).</summary>
public sealed record TwoFactorDeliveryInfo(
    string Channel,
    string MaskedDestination,
    bool RequiresPhoneEnrollment,
    bool EmailAvailable,
    bool SmsAvailable);
