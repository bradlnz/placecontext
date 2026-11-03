using PlaceContext.Application.Cqrs;
using PlaceContext.Application.DatabaseInspection;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel : PageViewModel
{
    private readonly PlaceContextService _svc;
    private readonly PortalUiState _ui;
    private readonly IJSRuntime _js;
    private readonly IDispatcher? _dispatcher;
    public bool ApplicationDatabase { get; private set; }
    public IReadOnlyList<DatabaseTable> ApplicationTables { get; private set; } = [];

    public ProjectDataViewModel(PlaceContextService svc, PortalUiState ui, IJSRuntime js, IDispatcher? dispatcher = null)
    {
        _svc = svc;
        _ui = ui;
        _js = js;
        _dispatcher = dispatcher;
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId
    {
        get; private set;
    }
    public string? Error
    {
        get; private set;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId, bool applicationDatabase = false)
    {
        _ui.Set("Data", applicationDatabase ? "PlaceContext database" : "the project's own database");
        if (ProjectId != projectId || ApplicationDatabase != applicationDatabase)
        {
            ProjectId = projectId;
            ApplicationDatabase = applicationDatabase;
            ApplicationTables = [];
            Tables = [];
            TablesReady = false;
            Error = null;
            ViewMonacoReady = false;
            ResetStudioResources();
        }
        ResetSqlEditor();
    }

    public static bool IsJsonCell(string column, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        var candidate = value.Trim();
        if (
            !column.Contains("json", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith('{')
            && !candidate.StartsWith('[')
        )
        {
            return false;
        }
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
        if (ShowNewView || ShowTableModal || SqlEditorActiveTable is not null)
        {
            _ = PushSqlSchemaAsync();
        }
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
                {
                    ViewMonaco = false;
                }
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
                {
                    CloseTableModal();
                }
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
                {
                    SqlEditorMonaco = false;
                }
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
        NotifyStateChanged();
    }

    public async Task RefreshTablesAsync()
    {
        try
        {
            Error = null;
            Tables = [];
            ApplicationTables = [];
            if (ApplicationDatabase)
            {
                ApplicationTables = await _dispatcher!.Query(new ListDatabaseTablesQuery());
                Tables = ApplicationTables.Select(table => new ProjectTableInfo(table.Name, 0, ReadOnly: true)).ToArray();
                return;
            }
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

    public bool ShowTableModal
    {
        get; set;
    }
    public string? ModalTableName
    {
        get; private set;
    }
    public string ModalSql { get; set; } = "";
    public ProjectQueryResult? ModalResult
    {
        get; private set;
    }
    public string? ModalError
    {
        get; private set;
    }
    public bool ModalRunning
    {
        get; private set;
    }
    public bool ModalEditorReady
    {
        get; set;
    }
    public string? JsonViewerTitle
    {
        get; private set;
    }
    public string? JsonViewerValue
    {
        get; private set;
    }

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

    public async Task<string?> TablePreviewSqlAsync(string table)
    {
        try
        {
            if (ApplicationDatabase)
            {
                var applicationTable = ApplicationTables.Single(item => item.Name == table);
                var qualified = string.Join(".", applicationTable.Name.Split('.').Select(Quote));
                return $"SELECT * FROM {qualified} LIMIT 50;";
            }
            var columns = await _svc.ListProjectTableColumnsAsync(ProjectId, table);
            if (columns.Count == 0)
            {
                throw new InvalidOperationException($"Table {table} has no readable columns.");
            }
            static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
            Error = null;
            return $"SELECT {string.Join(", ", columns.Select(column => Quote(column.Name)))} FROM {Quote(table)} LIMIT 50;";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            NotifyStateChanged();
            return null;
        }
    }

    public async Task OpenTableModalAsync(string table)
    {
        var sql = await TablePreviewSqlAsync(table);
        if (sql is null)
        {
            return;
        }
        ModalTableName = table;
        ModalSql = sql;
        ModalResult = null;
        ModalError = null;
        ModalRunning = false;
        ModalEditorReady = false;
        ShowTableModal = true;
        NotifyStateChanged();
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
        {
            return "";
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", ModalResult.Columns.Select(CsvEscape)));
        foreach (var row in ModalResult.Rows)
        {
            sb.AppendLine(string.Join(",", row.Select(v => CsvEscape(v ?? ""))));
        }
        return "data:text/csv;charset=utf-8;base64,"
                    + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
