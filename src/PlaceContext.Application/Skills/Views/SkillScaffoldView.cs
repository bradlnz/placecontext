namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the result of scaffolding a Claude Code skill.</summary>
public sealed record SkillScaffoldView(string Name, string Path, string Markdown);
