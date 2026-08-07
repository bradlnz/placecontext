using Microsoft.JSInterop;
using PlaceContext.Application;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Pushes the project's SQL schema (tables + columns, and optionally OpenSearch indexes) into
/// Monaco's autocomplete provider. Safe to call repeatedly and from any view model that hosts a
/// SQL Monaco editor.
/// </summary>
public static class SqlSchemaHelper
{
    public static async Task PushAsync(
        IPlaceContextService svc,
        IJSRuntime js,
        Guid projectId,
        bool includeIndexes = false)
    {
        var tables = new List<object>();
        try
        {
            var tableInfos = await svc.ListProjectDataTablesAsync(projectId);
            foreach (var t in tableInfos)
            {
                var columns = await svc.ListProjectTableColumnsAsync(projectId, t.Name);
                tables.Add(new
                {
                    name = t.Name,
                    columns = columns.Select(c => new { name = c.Name, type = c.Type }).ToList(),
                });
            }
        }
        catch
        {
            // Schema is best-effort; the editor still works without it.
            return;
        }

        var indexes = new List<object>();
        if (includeIndexes)
        {
            try
            {
                var idxs = await svc.ListOpenSearchIndicesAsync(projectId);
                foreach (var i in idxs)
                {
                    var fields = new List<object>();
                    try
                    {
                        var fieldInfos = await svc.ListOpenSearchFieldsAsync(projectId, i.Name);
                        foreach (var f in fieldInfos)
                            fields.Add(new { name = f.Name, type = f.Type });
                    }
                    catch { }
                    indexes.Add(new { name = i.Name, columns = fields });
                }
            }
            catch { }
        }

        try
        {
            await js.InvokeVoidAsync("pcmonaco.setSqlSchema", new { tables, indexes });
        }
        catch { }
    }
}
