namespace PlaceContext.Host.Controllers.Api.Records;
public sealed record AgentExchangeResult(
    string JoinCode, string ServerUrl, string Command
);
