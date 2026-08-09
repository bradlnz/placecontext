using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Bootstrap a repository into PlaceContext in one shot: create the project, backfill its change ledger
/// from git history, seed context from the repo's README/AGENTS/CLAUDE, and scaffold local skills and
/// agent definitions for the target AI agent.
/// </summary>
public sealed record OnboardCommand(string Path, string? Name, string Agent = "claude", int BackfillLimit = 50)
    : ICommand<OnboardResultView>;
