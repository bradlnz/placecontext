using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Replace a project's own code-requirements document (added on top of the global one).</summary>
public sealed record SetProjectRequirementsCommand(Guid ProjectId, string Markdown) : ICommand<CodeRequirementsView>;
