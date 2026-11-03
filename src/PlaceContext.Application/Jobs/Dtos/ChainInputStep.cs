namespace PlaceContext.Application.Dtos;

public sealed record ChainInputStep(int Index, string JobName, IReadOnlyList<JobParameterDto> Parameters,
    IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode?>? Values = null);
