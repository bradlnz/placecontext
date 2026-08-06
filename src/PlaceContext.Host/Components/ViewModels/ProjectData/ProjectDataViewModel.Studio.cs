using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>Which resource list the SQL Studio sidebar is showing.</summary>
public enum SidebarPane
{
    Tables,
    Indexes,
    Queries,
}

public sealed partial class ProjectDataViewModel
{
    // ── Sidebar panes ────────────────────────────────────────────────────────────────────────
    public SidebarPane SidebarPane { get; private set; } = SidebarPane.Tables;

    public void ShowPane(SidebarPane pane)
    {
        if (SidebarPane == pane)
            return;
        SidebarPane = pane;
        NotifyStateChanged();
        if (pane == SidebarPane.Indexes && !IndicesReady && IndicesError is null)
            _ = LoadIndicesAsync();
        else if (pane == SidebarPane.Queries && !SavedQueriesReady)
            _ = LoadSavedQueriesAsync();
    }

    // ── Indexes (OpenSearch) ─────────────────────────────────────────────────────────────────
    public IReadOnlyList<OpenSearchIndexView> Indices { get; private set; } =
        Array.Empty<OpenSearchIndexView>();
    public bool IndicesReady { get; private set; }
    public string? IndicesError { get; private set; }

    public async Task LoadIndicesAsync()
    {
        IndicesReady = false;
        IndicesError = null;
        NotifyStateChanged();
        try
        {
            Indices = await _svc.ListOpenSearchIndicesAsync(ProjectId);
        }
        catch (Exception ex)
        {
            IndicesError = Trim(ex.Message);
        }
        finally
        {
            IndicesReady = true;
            NotifyStateChanged();
        }
    }

    // ── Materialize index → Postgres table ───────────────────────────────────────────────────
    public bool ShowMaterializeDialog { get; private set; }
    public string? MaterializeIndexName { get; private set; }
    public string MaterializeTableName { get; set; } = "";
    public bool Materializing { get; private set; }
    public string? MaterializeError { get; private set; }
    public string? MaterializeMessage { get; private set; }

    public void OpenMaterializeDialog(string indexName)
    {
        MaterializeIndexName = indexName;
        MaterializeTableName = MaterializeIndexTableCommand.DefaultTableName(indexName);
        MaterializeError = null;
        ShowMaterializeDialog = true;
        NotifyStateChanged();
    }

    public void CloseMaterializeDialog()
    {
        ShowMaterializeDialog = false;
        MaterializeError = null;
        NotifyStateChanged();
    }

    public async Task MaterializeAsync()
    {
        if (string.IsNullOrWhiteSpace(MaterializeTableName))
        {
            MaterializeError = "Give the table a name.";
            NotifyStateChanged();
            return;
        }
        Materializing = true;
        MaterializeError = null;
        try
        {
            var result = await _svc.MaterializeIndexTableAsync(
                ProjectId, MaterializeIndexName!, MaterializeTableName.Trim());
            ShowMaterializeDialog = false;
            MaterializeMessage =
                $"{result.SourceIndex} → {result.TableName}: {result.RowsImported:N0} row(s), {result.ColumnCount} column(s)"
                + (result.Truncated ? " (capped — index has more rows)" : "") + ".";
            await RefreshTablesAsync();
        }
        catch (Exception ex)
        {
            MaterializeError = Trim(ex.Message);
        }
        finally
        {
            Materializing = false;
            NotifyStateChanged();
        }
    }

    // ── Saved queries ────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<SavedQueryRecord> SavedQueries { get; private set; } =
        Array.Empty<SavedQueryRecord>();
    public bool SavedQueriesReady { get; private set; }
    public string? SavedQueriesError { get; private set; }

    public bool ShowSaveQueryDialog { get; private set; }
    public string SaveQueryName { get; set; } = "";
    public string? SaveQueryError { get; private set; }
    public bool SavingQuery { get; private set; }

    public async Task LoadSavedQueriesAsync()
    {
        SavedQueriesReady = false;
        SavedQueriesError = null;
        NotifyStateChanged();
        try
        {
            SavedQueries = await _svc.ListSavedQueriesAsync(ProjectId);
        }
        catch (Exception ex)
        {
            SavedQueriesError = Trim(ex.Message);
        }
        finally
        {
            SavedQueriesReady = true;
            NotifyStateChanged();
        }
    }

    public void OpenSaveQueryDialog(string defaultName)
    {
        SaveQueryName = defaultName;
        SaveQueryError = null;
        ShowSaveQueryDialog = true;
        NotifyStateChanged();
    }

    public void CloseSaveQueryDialog()
    {
        ShowSaveQueryDialog = false;
        SaveQueryError = null;
        NotifyStateChanged();
    }

    public async Task SaveQueryAsync(Func<string, Task<string?>>? getSql)
    {
        if (string.IsNullOrWhiteSpace(SaveQueryName))
        {
            SaveQueryError = "Give the query a name.";
            NotifyStateChanged();
            return;
        }
        SavingQuery = true;
        SaveQueryError = null;
        try
        {
            var sql = getSql is not null ? await getSql(SqlEditorId) : null;
            if (string.IsNullOrWhiteSpace(sql))
            {
                SaveQueryError = "The editor is empty.";
                return;
            }
            await _svc.SaveSavedQueryAsync(ProjectId, SaveQueryName.Trim(), sql);
            ShowSaveQueryDialog = false;
            SaveQueryName = "";
            await LoadSavedQueriesAsync();
        }
        catch (Exception ex)
        {
            SaveQueryError = Trim(ex.Message);
        }
        finally
        {
            SavingQuery = false;
            NotifyStateChanged();
        }
    }

    public async Task DeleteSavedQueryAsync(Guid id)
    {
        try
        {
            await _svc.DeleteSavedQueryAsync(id);
            await LoadSavedQueriesAsync();
        }
        catch (Exception ex)
        {
            SavedQueriesError = Trim(ex.Message);
            NotifyStateChanged();
        }
    }

    // ── SQL schema for Monaco autocomplete ───────────────────────────────────────────────────
    // Pushed once per project session: table names + columns feed the completion provider.
    private bool _sqlSchemaPushed;

    public async Task PushSqlSchemaAsync()
    {
        if (_sqlSchemaPushed || !SqlEditorMonaco)
            return;
        try
        {
            var tables = new List<object>(Tables.Count);
            foreach (var table in Tables)
            {
                var columns = await _svc.ListProjectTableColumnsAsync(ProjectId, table.Name);
                tables.Add(new
                {
                    name = table.Name,
                    columns = columns.Select(c => new { name = c.Name, type = c.Type }).ToList(),
                });
            }
            await _js.InvokeVoidAsync("pcmonaco.setSqlSchema", tables);
            _sqlSchemaPushed = true;
        }
        catch
        {
            // Schema is best-effort; the editor still works without it.
        }
    }
}
