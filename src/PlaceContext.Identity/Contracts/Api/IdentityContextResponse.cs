namespace PlaceContext.Identity.Contracts.Api;

public sealed record IdentityContextResponse(
    bool Configured,
    string AntiforgeryFieldName,
    string AntiforgeryToken);
