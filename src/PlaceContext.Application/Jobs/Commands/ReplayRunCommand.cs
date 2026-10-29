using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Replay a prior run: re-execute the exact workload snapshot it captured. Returns the new run.</summary>
public sealed record ReplayRunCommand(Guid RunId, Guid? NewRunId = null) : ICommand<JobRunDetailView>;
