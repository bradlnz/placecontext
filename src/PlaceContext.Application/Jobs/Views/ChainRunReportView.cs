namespace PlaceContext.Application.Dtos;

/// <summary>One chain run with its project's name resolved — the cross-project chain history list
/// (Observability's "Chains" tab). Mirrors <see cref="RunReportView"/> for job runs.</summary>
public sealed record ChainRunReportView(Guid ProjectId, string ProjectName, ChainRunView Run);
