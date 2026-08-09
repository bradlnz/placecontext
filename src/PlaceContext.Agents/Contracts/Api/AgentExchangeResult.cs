namespace PlaceContext.Agents.Contracts.Api;
public sealed record AgentExchangeResult(
    string JoinCode, string ServerUrl, string Command
);
