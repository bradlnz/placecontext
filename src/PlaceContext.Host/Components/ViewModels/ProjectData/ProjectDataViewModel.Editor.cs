using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    // ── Per-tab SQL editor (Monaco) ───────────────────────────────────────────────────────────
    // A single Monaco editor whose TextModels are keyed per table ("{editorId}::{tableName}" in
    // pcmonaco.js), so switching tabs is an instant model swap and each tab's edits survive
    // switching. When the CDN is unreachable, pcmonaco.init returns false and the razor falls
    // back to a two-way-bound <textarea>.
    public const string SqlEditorId = "pcdata-sql-editor";

    public bool SqlEditorMonaco { get; set; } = true;
    public bool SqlEditorReady { get; set; }
    public string? SqlEditorActiveTable { get; private set; }
    private string SqlEditorPendingValue { get; set; } = "";

    public void ResetSqlEditor()
    {
        SqlEditorReady = false;
        SqlEditorActiveTable = null;
        SqlEditorPendingValue = "";
        SqlEditorMonaco = true;
    }

    public async Task ShowSqlTabAsync(string tableName, string sql)
    {
        var switched = !StringComparer.Ordinal.Equals(SqlEditorActiveTable, tableName);
        SqlEditorActiveTable = tableName;
        SqlEditorPendingValue = sql;
        if (switched && SqlEditorMonaco && SqlEditorReady)
        {
            try
            {
                await _js.InvokeVoidAsync("pcmonaco.openFile", SqlEditorId, tableName, sql, "sql");
            }
            catch
            {
                SqlEditorMonaco = false;
            }
        }
    }

    public async Task SetSqlEditorValueAsync(string sql)
    {
        SqlEditorPendingValue = sql;
        if (SqlEditorMonaco && SqlEditorReady)
        {
            try
            {
                await _js.InvokeVoidAsync("pcmonaco.setValue", SqlEditorId, sql, "sql");
            }
            catch
            {
                SqlEditorMonaco = false;
            }
        }
    }

    public async Task<string?> GetSqlEditorValueAsync()
    {
        if (SqlEditorMonaco && SqlEditorReady)
        {
            try
            {
                var value = await _js.InvokeAsync<string>("pcmonaco.getValue", SqlEditorId);
                if (value is not null)
                    return value;
            }
            catch
            {
                SqlEditorMonaco = false;
            }
        }
        return null;
    }

    public async Task CloseSqlEditorFileAsync(string tableName)
    {
        if (SqlEditorMonaco && SqlEditorReady)
        {
            try
            {
                await _js.InvokeVoidAsync("pcmonaco.closeFile", SqlEditorId, tableName);
            }
            catch
            {
                // The model cache is best-effort; a later destroy()/re-init cleans up.
            }
        }
    }
}
