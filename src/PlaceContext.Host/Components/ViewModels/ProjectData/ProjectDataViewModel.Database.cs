using PlaceContext.Application.DatabaseInspection;
using PlaceContext.Application.ProjectData;
using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    public async Task<ProjectQueryResult> QueryApplicationDatabaseAsync(string sql)
    {
        try
        {
            return await _dispatcher!.Query(new QueryDatabaseQuery(sql));
        }
        catch (UnauthorizedAccessException)
        {
            ApplicationTables = [];
            Tables = [];
            Error = "Application tables are available only to owners and super admins; global tables expose their schema only.";
            throw;
        }
    }

    private async Task PushApplicationSchemaAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("pcmonaco.setSqlSchema", new
            {
                tables = ApplicationTables.Select(table => new
                {
                    name = table.Name,
                    columns = table.Columns.Select(column => new { name = column.Name, type = column.Type })
                }),
                indexes = Array.Empty<object>()
            });
        }
        catch (JSException)
        {
            // The text-area fallback does not need editor completions.
        }
    }
}
