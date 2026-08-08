using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    // ── Tables ────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<ProjectTableInfo> Tables { get; private set; } =
        Array.Empty<ProjectTableInfo>();
    public bool TablesReady { get; private set; }

    // ── Edit table ────────────────────────────────────────────────────────────────────────────
    public string? EditTable { get; private set; }
    public IReadOnlyList<ProjectColumnInfo> EditColumns { get; private set; } =
        Array.Empty<ProjectColumnInfo>();
    public string? EditError { get; private set; }
    public string? ConfirmDropColumn { get; set; }
    public ProjectDataColumnDraft AddCol { get; set; } = new();
    public bool AlteringColumn { get; private set; }

    // ── Edit table ────────────────────────────────────────────────────────────────────────────
    public async Task StartEditAsync(string table)
    {
        EditTable = table;
        EditError = null;
        ConfirmDropColumn = null;
        AddCol = new ProjectDataColumnDraft();
        await RefreshColumnsAsync();
        NotifyStateChanged();
    }

    public void CloseEdit()
    {
        EditTable = null;
        NotifyStateChanged();
    }

    private async Task RefreshColumnsAsync()
    {
        if (EditTable is null)
            return;
        try
        {
            EditColumns = await _svc.ListProjectTableColumnsAsync(ProjectId, EditTable);
        }
        catch (Exception ex)
        {
            EditError = Trim(ex.Message);
        }
    }

    public async Task AddColumnToTableAsync()
    {
        if (EditTable is null)
            return;
        EditError = null;
        if (string.IsNullOrWhiteSpace(AddCol.Name))
        {
            EditError = "Give the column a name.";
            NotifyStateChanged();
            return;
        }
        AlteringColumn = true;
        try
        {
            await _svc.AddProjectTableColumnAsync(
                ProjectId,
                EditTable,
                new ProjectColumnSpec(
                    AddCol.Name.Trim(),
                    AddCol.Type,
                    AddCol.NotNull,
                    PrimaryKey: false
                )
            );
            AddCol = new ProjectDataColumnDraft();
            await RefreshColumnsAsync();
            await RefreshTablesAsync();
        }
        catch (Exception ex)
        {
            EditError = Trim(ex.Message);
        }
        finally
        {
            AlteringColumn = false;
            NotifyStateChanged();
        }
    }

    public async Task DropColumnAsync(string column)
    {
        if (EditTable is null)
            return;
        EditError = null;
        ConfirmDropColumn = null;
        try
        {
            await _svc.DropProjectTableColumnAsync(ProjectId, EditTable, column);
            await RefreshColumnsAsync();
        }
        catch (Exception ex)
        {
            EditError = Trim(ex.Message);
        }
        NotifyStateChanged();
    }
}
