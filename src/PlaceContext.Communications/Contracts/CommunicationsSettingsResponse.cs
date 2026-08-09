using System.Text.Json;

namespace PlaceContext.Communications.Contracts;

public sealed record CommunicationsSettingsResponse(
    IReadOnlyList<CommunicationProviderView> Providers,
    JsonElement Projects);
