namespace PlaceContext.Host.Controllers.Api.Records;
public sealed record EntityRunJobRequest(string InputPayload, Guid RunId);