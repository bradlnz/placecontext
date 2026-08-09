namespace PlaceContext.Crm.Integration;

public interface ICrmDataClient
{
    Task InsertRowAsync(
        Guid projectId,
        string tableName,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default);
}
