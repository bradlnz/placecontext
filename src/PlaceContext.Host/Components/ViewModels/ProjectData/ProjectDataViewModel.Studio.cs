using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

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
    private bool _loadingIndices;
    private static readonly TimeSpan MaterializeIndexRefreshDelay = TimeSpan.FromMilliseconds(450);
    private const int MaterializeIndexRefreshAttempts = 6;

    // ── Sidebar panes ────────────────────────────────────────────────────────────────────────
    public SidebarPane SidebarPane { get; private set; } = SidebarPane.Tables;

    public void ShowPane(SidebarPane pane)
    {
        if (SidebarPane == pane)
            return;
        SidebarPane = pane;
        NotifyStateChanged();
        if (pane == SidebarPane.Indexes)
            _ = LoadIndicesAsync(force: true);
        else if (pane == SidebarPane.Queries && !SavedQueriesReady)
            _ = LoadSavedQueriesAsync();
    }

    // ── Indexes (OpenSearch) ─────────────────────────────────────────────────────────────────
    public IReadOnlyList<OpenSearchIndexView> Indices { get; private set; } =
        Array.Empty<OpenSearchIndexView>();
    public bool IndicesReady { get; private set; }
    public string? IndicesError { get; private set; }

    public async Task LoadIndicesAsync(bool force = false)
    {
        if (!force && IndicesReady && IndicesError is null)
            return;
        if (_loadingIndices)
            return;

        _loadingIndices = true;
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
            _loadingIndices = false;
            IndicesReady = true;
            NotifyStateChanged();
        }
    }

    // ── Materialize table → OpenSearch index ────────────────────────────────────────────────
    public bool ShowMaterializeDialog { get; private set; }
    public string? MaterializeTableName { get; private set; }
    public string MaterializeIndexName { get; set; } = "";
    public bool Materializing { get; private set; }
    public string? MaterializeError { get; private set; }
    public string? MaterializeMessage { get; private set; }

    public void OpenMaterializeDialog(string tableName)
    {
        MaterializeTableName = tableName;
        MaterializeIndexName = MaterializeTableIndexCommand.DefaultIndexName(tableName);
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
        if (string.IsNullOrWhiteSpace(MaterializeIndexName))
        {
            MaterializeError = "Give the index a name.";
            NotifyStateChanged();
            return;
        }
        Materializing = true;
        MaterializeError = null;
        try
        {
            var result = await _svc.MaterializeTableIndexAsync(
                ProjectId, MaterializeTableName!, MaterializeIndexName.Trim());
            ShowMaterializeDialog = false;
            MaterializeMessage =
                $"{result.SourceTable} → {result.IndexName}: {result.RowsIndexed:N0} row(s), {result.ColumnCount} column(s)"
                + (result.Truncated ? " (capped — table has more rows)" : "") + ".";
            await RefreshTablesAsync();
            await RefreshMaterializedIndexListingAsync(result.IndexName);
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

    private async Task RefreshMaterializedIndexListingAsync(string indexName)
    {
        await LoadIndicesAsync(force: true);
        if (Indices.Any(index => string.Equals(index.Name, indexName, StringComparison.Ordinal)))
            return;

        for (var attempt = 0; attempt < MaterializeIndexRefreshAttempts; attempt++)
        {
            await Task.Delay(MaterializeIndexRefreshDelay);
            await LoadIndicesAsync(force: true);
            if (Indices.Any(index => string.Equals(index.Name, indexName, StringComparison.Ordinal)))
                return;
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
        await SqlSchemaHelper.PushAsync(_svc, _js, ProjectId, includeIndexes: true);
        _sqlSchemaPushed = true;
    }
}
