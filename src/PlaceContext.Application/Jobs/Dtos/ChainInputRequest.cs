namespace PlaceContext.Application.Dtos;

public sealed record ChainInputRequest(Guid Id, IReadOnlyList<ChainInputStep> Steps,
    Guid? SubmittedBy = null, string? SubmittedValues = null);
public sealed record ChainInputStep(int Index, string JobName, IReadOnlyList<JobParameterDto> Parameters,
    IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode?>? Values = null);
