using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Build;

public interface IChainQuestionGenerator
{
    Task<string> GenerateAsync(LoopChainAction action, string? output, CancellationToken ct);
}
