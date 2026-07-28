using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;

    public ProjectDataViewModel(IPlaceContextService svc) => _svc = svc;

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        if (ProjectId != projectId)
        {
            ProjectId = projectId;
            MonacoReady = false;
            MonacoLite = false;
            ViewMonacoReady = false;
        }
    }

    public async Task LoadAsync()
    {
        await RefreshTablesAsync();
        NotifyStateChanged();
    }

    public async Task RefreshTablesAsync()
    {
        try { Tables = await _svc.ListProjectDataTablesAsync(ProjectId); }
        catch (Exception ex) { Error = ex.Message; }
        finally { TablesReady = true; NotifyStateChanged(); }
    }

    // ── Full-screen table query modal ─────────────────────────────────────────────────────────

    public const string TableModalEditorId = "pcdata-table-modal-editor";

    public bool ShowTableModal { get; set; }
    public string? ModalTableName { get; private set; }
    public string ModalSql { get; set; } = "";
    public ProjectQueryResult? ModalResult { get; private set; }
    public string? ModalError { get; private set; }
    public bool ModalRunning { get; private set; }
    public bool ModalEditorReady { get; set; }

    public void OpenTableModal(string table)
    {
        ModalTableName = table;
        ModalSql = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" LIMIT 100;";
        ModalResult = null;
        ModalError = null;
        ModalRunning = false;
        ModalEditorReady = false;
        ShowTableModal = true;
        NotifyStateChanged();
    }

    public void CloseTableModal()
    {
        ShowTableModal = false;
        ModalTableName = null;
        ModalSql = "";
        ModalResult = null;
        ModalError = null;
        ModalEditorReady = false;
    }

    public async Task RunModalAsync(Func<Task<string>> getSql)
    {
        ModalRunning = true;
        ModalError = null;
        try
        {
            var sql = await getSql();
            ModalResult = await _svc.ExecuteProjectDataAsync(ProjectId, sql);
        }
        catch (Exception ex) { ModalResult = null; ModalError = Trim(ex.Message); }
        finally { ModalRunning = false; NotifyStateChanged(); }
    }

    public string ModalResultCsvUri()
    {
        if (ModalResult is null) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", ModalResult.Columns.Select(CsvEscape)));
        foreach (var row in ModalResult.Rows)
            sb.AppendLine(string.Join(",", row.Select(v => CsvEscape(v ?? ""))));
        return "data:text/csv;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    }

}
