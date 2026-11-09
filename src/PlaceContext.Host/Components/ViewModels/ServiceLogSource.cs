namespace PlaceContext.Host.Components.ViewModels;

public sealed record ServiceLogSource(Guid Id, Guid ProjectId, string Name, string ProjectName, string Status);
