using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Replace a project's entire context document (set/overwrite), creating it if absent.</summary>
public sealed record SetContextCommand(Guid ProjectId, string Markdown) : ICommand<ProjectContextView>;
