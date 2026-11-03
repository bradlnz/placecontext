using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DatabaseInspection;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class DatabaseSettingsViewModel(IDispatcher dispatcher, PortalUiState ui) : PageViewModel
{
    public IReadOnlyList<DatabaseTable> Tables { get; private set; } = [];
    public DatabaseTable? Selected { get; private set; }
    public DatabaseTablePage? Page { get; private set; }
    public string Filter { get; set; } = "";
    public string? Error { get; private set; }
    public bool Busy { get; private set; }
    public IEnumerable<DatabaseTable> FilteredTables => Tables.Where(table =>
        table.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));
    public bool CanPrevious => !Busy && Page is { Offset: > 0 };
    public bool CanNext => !Busy && Page is { HasMore: true };
    public string RowSummary => Page is { Rows.Count: > 0 } page
        ? $"Rows {page.Offset + 1:N0}–{page.Offset + page.Rows.Count:N0} in this workspace"
        : "No records in this workspace.";

    public async Task LoadAsync()
    {
        if (Busy)
        {
            return;
        }
        ui.Set("PlaceContext database", "application tables and workspace records");
        Busy = true;
        Error = null;
        Tables = [];
        Selected = null;
        Page = null;
        NotifyStateChanged();
        try
        {
            Tables = await dispatcher.Query(new ListDatabaseTablesQuery());
        }
        catch (UnauthorizedAccessException)
        {
            Error = "Only workspace owners and super admins can view these tables.";
        }
        catch
        {
            Error = "Could not load the database tables. Try refreshing.";
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }

    public Task SelectAsync(DatabaseTable table) => ReadAsync(table, 0);
    public Task PreviousAsync() => CanPrevious && Selected is { } table
        ? ReadAsync(table, Math.Max(0, Page!.Offset - DatabaseTablePage.PageSize))
        : Task.CompletedTask;
    public Task NextAsync() => CanNext && Selected is { } table
        ? ReadAsync(table, checked(Page!.Offset + DatabaseTablePage.PageSize))
        : Task.CompletedTask;

    private async Task ReadAsync(DatabaseTable table, int offset)
    {
        if (Busy)
        {
            return;
        }
        Selected = table;
        Page = null;
        Error = null;
        if (!table.CanReadRows)
        {
            NotifyStateChanged();
            return;
        }
        Busy = true;
        NotifyStateChanged();
        try
        {
            Page = await dispatcher.Query(new ReadDatabaseTableQuery(table.Name, offset));
        }
        catch (UnauthorizedAccessException)
        {
            Tables = [];
            Selected = null;
            Error = "Your account no longer has access to these records.";
        }
        catch
        {
            Error = "Could not load this table. Select it again to retry.";
        }
        finally
        {
            Busy = false;
            NotifyStateChanged();
        }
    }
}
