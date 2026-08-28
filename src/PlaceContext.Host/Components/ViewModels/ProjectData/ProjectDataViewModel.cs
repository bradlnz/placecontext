using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel : PageViewModel
{
    private readonly PlaceContextService _svc;
    private readonly PortalUiState _ui;
    private readonly IJSRuntime _js;

    public ProjectDataViewModel(PlaceContextService svc, PortalUiState ui, IJSRuntime js)
    {
        _svc = svc;
        _ui = ui;
        _js = js;
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }
    public string? Error { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId)
    {
        _ui.Set("Data", "the project's own database");
        if (ProjectId != projectId)
        {
            ProjectId = projectId;
            ViewMonacoReady = false;
            ResetStudioResources();
        }
        ResetSqlEditor();
    }

    public static bool IsJsonCell(string column, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var candidate = value.Trim();
        if (
            !column.Contains("json", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith('{')
            && !candidate.StartsWith('[')
        )
            return false;
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(candidate);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool IsJsonCellPresentation(string column, string? value) => IsJsonCell(column, value);

    public async Task AfterRenderAsync()
    {
        if (ShowNewView && ViewMonaco && !ViewMonacoReady)
        {
            ViewMonacoReady = true;
            try
            {
                if (
                    !await _js.InvokeAsync<bool>(
                        "pcmonaco.init",
                        ViewEditorId,
                        NewViewSql,
                        "sql",
                        "vs-dark"
                    )
                )
                    ViewMonaco = false;
            }
            catch
            {
                ViewMonaco = false;
            }
        }
        if (ShowTableModal && !ModalEditorReady)
        {
            ModalEditorReady = true;
            try
            {
                if (
                    !await _js.InvokeAsync<bool>(
                        "pcmonaco.init",
                        TableModalEditorId,
                        ModalSql,
                        "sql",
                        "vs-dark"
                    )
                )
                    CloseTableModal();
            }
            catch
            {
                CloseTableModal();
            }
        }
        if (SqlEditorMonaco && !SqlEditorReady && SqlEditorActiveTable is not null)
        {
            SqlEditorReady = true;
            try
            {
                if (
                    !await _js.InvokeAsync<bool>(
                        "pcmonaco.init",
                        SqlEditorId,
                        SqlEditorPendingValue,
                        "sql",
                        "vs-dark",
                        SqlEditorActiveTable
                    )
                )
                    SqlEditorMonaco = false;
            }
            catch
            {
                SqlEditorMonaco = false;
            }
        }
    }

    public Task RunModalFromEditorAsync() =>
        RunModalAsync(() =>
            _js.InvokeAsync<string>("pcmonaco.getValue", TableModalEditorId).AsTask()
        );

    public Task SaveViewFromEditorAsync() =>
        SaveViewAsync(id => _js.InvokeAsync<string>("pcmonaco.getValue", id).AsTask());

    public Task ExportWithBrowserAsync(string table) =>
        ExportAsync(
            table,
            (name, uri) => _js.InvokeVoidAsync("pcdata.download", name, uri).AsTask()
        );

    public async Task ImportCsvAsync(IBrowserFile file)
    {
        try
        {
            using var reader = new StreamReader(file.OpenReadStream(MaxCsvBytes));
            var text = await reader.ReadToEndAsync();
            var records = ParseCsvRecords(text)
                .Where(r => r.Any(f => !string.IsNullOrEmpty(f)))
                .ToList();
            if (records.Count == 0)
            {
                SetCsvImport(new CsvImportDraft { FileName = file.Name });
                return;
            }
            var draft = new CsvImportDraft
            {
                FileName = file.Name,
                HasHeader = true,
                Records = records,
                TableName = SanitizeIdent(Path.GetFileNameWithoutExtension(file.Name), 0),
            };
            BuildColumns(draft);
            SetCsvImport(draft);
        }
        catch
        {
            SetCsvImport(new CsvImportDraft { FileName = file.Name });
        }
    }

    public async Task LoadAsync()
    {
        await RefreshTablesAsync();
        await LoadIndicesAsync();
        await PushSqlSchemaAsync();
        NotifyStateChanged();
    }

    public async Task RefreshTablesAsync()
    {
        try
        {
            Tables = await _svc.ListProjectDataTablesAsync(ProjectId);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            TablesReady = true;
            NotifyStateChanged();
        }
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
    public string? JsonViewerTitle { get; private set; }
    public string? JsonViewerValue { get; private set; }

    public void OpenJsonViewer(string column, string value)
    {
        JsonViewerTitle = column;
        using var document = System.Text.Json.JsonDocument.Parse(value);
        JsonViewerValue = System.Text.Json.JsonSerializer.Serialize(
            document.RootElement,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
        );
    }

    public void CloseJsonViewer()
    {
        JsonViewerTitle = null;
        JsonViewerValue = null;
    }

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

    public async Task OpenTableModalAsync(string table)
    {
        OpenTableModal(table);
        await RunModalAsync(() => Task.FromResult(ModalSql));
    }

    public void CloseTableModal()
    {
        CloseJsonViewer();
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
        catch (Exception ex)
        {
            ModalResult = null;
            ModalError = Trim(ex.Message);
        }
        finally
        {
            ModalRunning = false;
            NotifyStateChanged();
        }
    }

    public string ModalResultCsvUri()
    {
        if (ModalResult is null)
            return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", ModalResult.Columns.Select(CsvEscape)));
        foreach (var row in ModalResult.Rows)
            sb.AppendLine(string.Join(",", row.Select(v => CsvEscape(v ?? ""))));
        return "data:text/csv;charset=utf-8;base64,"
            + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
