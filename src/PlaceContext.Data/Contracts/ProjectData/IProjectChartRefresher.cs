namespace PlaceContext.Application.Ports;

public interface IProjectChartRefresher
{
    Task RefreshProjectAsync(Guid projectId, CancellationToken ct = default);
    Task RefreshTableAsync(Guid projectId, string tableName, string? instruction, CancellationToken ct = default);
}
