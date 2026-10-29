using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Api;

/// <summary>One source file in a code workload (map or reduce step).</summary>
public sealed record JobCodeFile(string Path, string Content);
