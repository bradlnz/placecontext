namespace PlaceContext.AgentChat.Integration;

public sealed record AgentChatTablePage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    long TotalCount);
