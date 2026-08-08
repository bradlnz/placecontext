namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobChainsPageResponse(
    IReadOnlyList<JobChainJobResponse> Jobs,
    IReadOnlyList<JobChainResponse> Chains,
    bool CanSendEmail,
    bool CanSendSms);
