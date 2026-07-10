using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Run a chain now: each step's primary output (reduce artifact if present, else the shard artifacts)
/// becomes the next step's input payload. <paramref name="InputPayload"/> feeds the FIRST step; when
/// null the first job runs with its stored shard payloads.
/// </summary>
public sealed record RunJobChainCommand(Guid ChainId, string? InputPayload = null)
    : ICommand<ChainRunView>;
