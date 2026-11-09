using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Register a project explicitly through the MCP (replacing folder discovery). Idempotent by path:
/// re-creating a known path returns the existing project. New projects are created already Registered
/// so they are immediately usable.
/// </summary>
public sealed record CreateProjectCommand(string Path, string? Name) : ICommand<ProjectSummaryView>;
