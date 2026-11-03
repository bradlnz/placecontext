namespace PlaceContext.Application.DatabaseInspection;

public sealed record DatabaseTablePage(DatabaseTable Table, IReadOnlyList<IReadOnlyList<string?>> Rows,
    int Offset, bool HasMore)
{
    public const int PageSize = 50;
}
