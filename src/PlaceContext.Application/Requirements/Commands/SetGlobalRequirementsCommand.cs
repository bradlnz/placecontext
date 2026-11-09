using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>Replace the global requirements document (applies to every project).</summary>
public sealed record SetGlobalRequirementsCommand(string Markdown) : ICommand<RequirementsView>;
