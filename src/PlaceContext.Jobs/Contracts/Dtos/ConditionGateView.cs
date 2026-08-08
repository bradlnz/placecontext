using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

/// <summary>Conditional routing gate.</summary>
public sealed record ConditionGateView(string Expression, IReadOnlyList<JobChainStageView>? ElseBranch = null) : ChainGateView;
