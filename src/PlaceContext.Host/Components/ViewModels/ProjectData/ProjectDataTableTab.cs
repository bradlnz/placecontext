using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectDataTableTab
{
    public ProjectDataTableTab(
        string tableName,
        string sql,
        ProjectDataTableTabSource source = ProjectDataTableTabSource.Project
    )
    {
        TableName = tableName;
        Sql = sql;
        Source = source;
    }

    public string TableName { get; }
    public ProjectDataTableTabSource Source { get; }
    public bool IsIndex => Source == ProjectDataTableTabSource.OpenSearch;
    public string Sql { get; set; }
    public ProjectQueryResult? Result { get; set; }
    public string? Error { get; set; }
    public bool Running { get; set; }
}
