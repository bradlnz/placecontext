namespace PlaceContext.Host.Components.ViewModels;

public sealed record CommunicationKindOption(
    CommunicationProviderKind Kind,
    CommunicationChannel Channel
);
