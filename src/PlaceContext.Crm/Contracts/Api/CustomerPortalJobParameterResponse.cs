namespace PlaceContext.Crm.Contracts.Api;

public sealed record CustomerPortalJobParameterResponse(
    string Name,
    string? Label = null,
    bool Required = true,
    string Type = "string",
    IReadOnlyList<string>? Options = null);
