namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobChainsPageResponse(
    IReadOnlyList<JobChainJobResponse> Jobs,
    IReadOnlyList<JobChainResponse> Chains,
    bool CanSendEmail,
    bool CanSendSms);
