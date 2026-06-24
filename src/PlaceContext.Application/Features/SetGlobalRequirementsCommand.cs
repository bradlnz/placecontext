using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Replace the global code-requirements document (applies to every project).</summary>
public sealed record SetGlobalRequirementsCommand(string Markdown) : ICommand<CodeRequirementsView>;
