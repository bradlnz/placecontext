namespace PlaceContext.Crm.Contracts.Api;

public sealed record SendCrmCommunicationRequest(string Channel, string? Subject, string Body);
