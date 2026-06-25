using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Append a Markdown section to a project's context document (creating it if absent).</summary>
public sealed record AddContextCommand(Guid ProjectId, string Section) : ICommand<ProjectContextView>;
