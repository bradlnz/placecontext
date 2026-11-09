namespace PlaceContext.Host.Components.ViewModels;

public sealed record JobLogEvent(int Index, string Stream, string? Timestamp, string Level, string Message);
