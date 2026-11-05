namespace PlaceContext.Application.Dtos;

public sealed record ChainInputRequest(Guid Id, IReadOnlyList<ChainInputStep> Steps,
    Guid? SubmittedBy = null, string? SubmittedValues = null,
    string? Kind = null, string? Instructions = null, string ResponseType = "text", bool RunAutomatically = false);
