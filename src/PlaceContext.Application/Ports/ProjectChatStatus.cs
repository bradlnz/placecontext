namespace PlaceContext.Application.Ports;

public sealed record ProjectChatStatus(ProjectChatBackend Backend, bool IsEnabled, string Label);
