namespace PlaceContext.Application.Ports;

/// <summary>Temporary platform seam for chart refresh while the analytics worker moves to Data.</summary>
public interface IProjectChartRefresher
{
    Task RefreshProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task RefreshTableAsync(
        Guid projectId,
        string tableName,
        string? instruction = null,
        CancellationToken cancellationToken = default);
}
