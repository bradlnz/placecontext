using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Workflows;

public sealed record WorkflowReadinessFinding(string Severity, string Title, string Detail);
