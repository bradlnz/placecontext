using PlaceContext.Application.SavedQueries;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    private void ResetStudioResources()
    {
        SidebarPane = SidebarPane.Tables;
        SavedQueries = Array.Empty<SavedQueryRecord>();
        SavedQueriesReady = false;
        SavedQueriesError = null;
        _sqlSchemaPushed = false;
    }

    // ── Sidebar panes ────────────────────────────────────────────────────────────────────────
    public SidebarPane SidebarPane { get; private set; } = SidebarPane.Tables;

    public void ShowPane(SidebarPane pane)
    {
        if (SidebarPane == pane)
        {
            return;
        }
        SidebarPane = pane;
        NotifyStateChanged();
        if (pane == SidebarPane.Queries && !SavedQueriesReady)
        {
            _ = LoadSavedQueriesAsync();
        }
    }

    // ── Saved queries ────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<SavedQueryRecord> SavedQueries
    {
        get; private set;
    } =
        Array.Empty<SavedQueryRecord>();
    public bool SavedQueriesReady
    {
        get; private set;
    }
    public string? SavedQueriesError
    {
        get; private set;
    }

    public bool ShowSaveQueryDialog
    {
        get; private set;
    }
    public string SaveQueryName { get; set; } = "";
    public string? SaveQueryError
    {
        get; private set;
    }
    public bool SavingQuery
    {
        get; private set;
    }

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
        if (!SqlEditorMonaco || _sqlSchemaPushed)
        {
            return;
        }
        _sqlSchemaPushed = true;
        if (ApplicationDatabase)
        {
            await PushApplicationSchemaAsync();
            return;
        }
        await SqlSchemaHelper.PushAsync(_svc, _js, ProjectId, includeIndexes: false);
    }
}
