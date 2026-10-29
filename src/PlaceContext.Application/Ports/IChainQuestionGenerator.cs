using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

public interface IChainQuestionGenerator
{
    Task<string> GenerateAsync(LoopChainAction action, string? output, CancellationToken ct);
}
