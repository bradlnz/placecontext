namespace PlaceContext.Application.Dtos;

/// <summary>Read model: the cross-project Change Activity feed.</summary>
public sealed record RootActivityView(IReadOnlyList<ActivityEntryView> Entries);
