using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Application.Dtos;

public sealed record ContainerBuildChainActionView(string Dockerfile, string ContextField, string OutputField, int TimeoutSeconds, bool AllowNetwork)
    : ChainActionView(ContainerBuildChainAction.ActionType, "Build container");
