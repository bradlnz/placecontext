namespace PlaceContext.Host.Controllers;

public sealed record IdentityContextResponse(
    bool Configured,
    string AntiforgeryFieldName,
    string AntiforgeryToken);
