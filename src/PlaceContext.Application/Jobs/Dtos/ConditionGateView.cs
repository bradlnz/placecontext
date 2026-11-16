namespace PlaceContext.Application.Dtos;

/// <summary>Conditional routing gate.</summary>
public sealed record ConditionGateView(string Expression, IReadOnlyList<JobChainStageView>? ElseBranch = null, string? ReviewQuestion = null) : ChainGateView;
