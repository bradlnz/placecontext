using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    // ── New table wizard ──────────────────────────────────────────────────────────────────────
    public sealed class ColumnDraft { public string Name = ""; public string Type = DataColumnTypes.Text; public bool NotNull; public bool PrimaryKey; }
    public sealed class TableDraft { public string Name = ""; public List<ColumnDraft> Columns = new(); }
    public static readonly string[] ColumnTypes = DataColumnTypes.All.ToArray();

    public TableDraft? NewTable { get; private set; }
    public string? NewTableError { get; private set; }
    public bool Creating { get; private set; }

    // ── Views ─────────────────────────────────────────────────────────────────────────────────
    public bool ShowNewView { get; private set; }
    public string NewViewName { get; set; } = "";
    public string NewViewSql { get; set; } = "";
    public string? ViewError { get; private set; }
    public bool ViewMonaco { get; set; } = true;
    public bool ViewMonacoReady { get; set; }
    public const string ViewEditorId = "pcmonaco-newview";

    // ── CSV import ────────────────────────────────────────────────────────────────────────────
    public sealed class CsvImportDraft
    {
        public string FileName = "";
        public string TableName = "";
        public bool HasHeader = true;
        public List<ColumnDraft> Columns = new();
        public List<string[]> Records = new();
        public IEnumerable<string[]> DataRecords => HasHeader ? Records.Skip(1) : Records;
        public int DataRowCount => Math.Max(0, HasHeader ? Records.Count - 1 : Records.Count);
        public IEnumerable<string[]> PreviewRows() => DataRecords.Take(8);
    }
    public CsvImportDraft? CsvImport { get; private set; }
    public string? CsvError { get; private set; }
    public IReadOnlyList<string> ImportWarnings { get; private set; } = Array.Empty<string>();
    public bool Importing { get; private set; }
    public const long MaxCsvBytes = 15L * 1024 * 1024;

    // ── Rename / drop ─────────────────────────────────────────────────────────────────────────
    public string? Renaming { get; private set; }
    public string? RenameTo { get; set; }
    public string? Dropping { get; private set; }

    // ── SQL execution ─────────────────────────────────────────────────────────────────────────
    public async Task RunAsync(Func<Task<string>> getSql)
    {
        Running = true;
        Error = null;
        try
        {
            var sql = await getSql();
            Result = await _svc.ExecuteProjectDataAsync(ProjectId, sql);
            await RefreshTablesAsync();
        }
        catch (Exception ex) { Result = null; Error = Trim(ex.Message); }
        finally { Running = false; NotifyStateChanged(); }
    }

    public async Task SelectTableAsync(string table, Func<string, Task> setSqlAndRun)
    {
        var sql = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\" LIMIT 100;";
        await setSqlAndRun(sql);
    }

    // ── New table wizard ──────────────────────────────────────────────────────────────────────
    public void StartNewTable()
    {
        NewTableError = null;
        NewTable = new TableDraft { Columns = { new ColumnDraft { Name = "id", Type = DataColumnTypes.Uuid, NotNull = true, PrimaryKey = true } } };
        NotifyStateChanged();
    }

    public void AddColumn() { NewTable?.Columns.Add(new ColumnDraft()); NotifyStateChanged(); }

    public async Task CreateTableAsync()
    {
        if (NewTable is null) return;
        NewTableError = null;
        if (string.IsNullOrWhiteSpace(NewTable.Name)) { NewTableError = "Give the table a name."; NotifyStateChanged(); return; }
        var cols = NewTable.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
        if (cols.Count == 0) { NewTableError = "Add at least one named column."; NotifyStateChanged(); return; }

        Creating = true;
        try
        {
            var specs = cols.Select(c => new ProjectColumnSpec(c.Name.Trim(), c.Type, c.NotNull, c.PrimaryKey)).ToList();
            await _svc.CreateProjectTableAsync(ProjectId, NewTable.Name.Trim(), specs);
            NewTable = null;
            await RefreshTablesAsync();
        }
        catch (Exception ex) { NewTableError = Trim(ex.Message); }
        finally { Creating = false; NotifyStateChanged(); }
    }

    public void CloseNewTable() { NewTable = null; NotifyStateChanged(); }

    // ── CSV import ────────────────────────────────────────────────────────────────────────────
    public void PrepareCsvImport(string fileName, Stream stream)
    {
        // Note: actual file reading is done in the razor file (InputFile API).
        // This method is called after parsing.
    }

    public void SetCsvImport(CsvImportDraft draft) { CsvImport = draft; CsvError = null; NotifyStateChanged(); }
    public void CloseCsvImport() { CsvImport = null; NotifyStateChanged(); }

    public void OnHeaderToggled(bool hasHeader)
    {
        if (CsvImport is null) return;
        CsvImport.HasHeader = hasHeader;
        BuildColumns(CsvImport);
        NotifyStateChanged();
    }

    public async Task RunImportAsync(Func<Task<string>>? getMonacoValue = null)
    {
        if (CsvImport is null) return;
        CsvError = null;
        var d = CsvImport;
        if (string.IsNullOrWhiteSpace(d.TableName)) { CsvError = "Give the table a name."; NotifyStateChanged(); return; }
        if (d.Columns.Any(c => string.IsNullOrWhiteSpace(c.Name))) { CsvError = "Every column needs a name."; NotifyStateChanged(); return; }

        Importing = true;
        try
        {
            var specs = d.Columns.Select(c => new ProjectColumnSpec(c.Name.Trim(), c.Type, NotNull: false, PrimaryKey: false)).ToList();
            var count = d.Columns.Count;
            var rows = d.DataRecords
                .Select(r => (IReadOnlyList<string?>)Enumerable.Range(0, count)
                    .Select(i => i < r.Length && !string.IsNullOrEmpty(r[i]) ? r[i] : null).ToList())
                .ToList();
            var table = d.TableName.Trim();
            var result = await _svc.ImportCsvToProjectTableAsync(ProjectId, table, specs, rows, createTable: true);
            CsvImport = null;
            ImportWarnings = result.DuplicateWarnings;
            await RefreshTablesAsync();
        }
        catch (Exception ex) { CsvError = Trim(ex.Message); }
        finally { Importing = false; NotifyStateChanged(); }
    }

    public void DismissImportWarnings() { ImportWarnings = Array.Empty<string>(); NotifyStateChanged(); }

    public static void BuildColumns(CsvImportDraft d)
    {
        var width = d.Records.Max(r => r.Length);
        var header = d.Records[0];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cols = new List<ColumnDraft>();
        for (var c = 0; c < width; c++)
        {
            var raw = d.HasHeader && c < header.Length ? header[c] : $"col{c + 1}";
            var name = SanitizeIdent(raw, c);
            var baseName = name;
            var n = 2;
            while (!used.Add(name)) name = $"{baseName}_{n++}";
            var sample = d.DataRecords.Take(200).Select(r => c < r.Length ? r[c] : null);
            cols.Add(new ColumnDraft { Name = name, Type = InferType(sample) });
        }
        d.Columns = cols;
    }

    // ── Views ─────────────────────────────────────────────────────────────────────────────────
    public void OpenNewView() { ShowNewView = true; ViewMonacoReady = false; ViewError = null; NotifyStateChanged(); }
    public void CloseNewView() { ShowNewView = false; NotifyStateChanged(); }

    public async Task SaveViewAsync(Func<string, Task<string>>? getMonacoValue = null)
    {
        ViewError = null;
        try
        {
            if (ViewMonaco && ViewMonacoReady && getMonacoValue is not null)
                NewViewSql = await getMonacoValue(ViewEditorId);
            await _svc.SaveProjectViewAsync(ProjectId, NewViewName, NewViewSql);
            ShowNewView = false;
            NewViewName = "";
            NewViewSql = "";
            await RefreshTablesAsync();
        }
        catch (Exception ex) { ViewError = ex.Message; NotifyStateChanged(); }
    }

    public async Task DropViewAsync(string name)
    {
        try { await _svc.DropProjectViewAsync(ProjectId, name); await RefreshTablesAsync(); }
        catch (Exception ex) { ViewError = ex.Message; NotifyStateChanged(); }
    }

    // ── Rename / drop ─────────────────────────────────────────────────────────────────────────
    public void StartRename(string table) { Dropping = null; Renaming = table; RenameTo = table; NotifyStateChanged(); }
    public void StartDrop(string table) { Renaming = null; Dropping = table; NotifyStateChanged(); }
    public void CancelRename() { Renaming = null; NotifyStateChanged(); }
    public void CancelDrop() { Dropping = null; NotifyStateChanged(); }

    public async Task ConfirmRenameAsync()
    {
        if (Renaming is null || string.IsNullOrWhiteSpace(RenameTo)) return;
        try
        {
            await _svc.RenameProjectTableAsync(ProjectId, Renaming, RenameTo.Trim());
            Renaming = null;
            await RefreshTablesAsync();
        }
        catch (Exception ex) { Error = Trim(ex.Message); NotifyStateChanged(); }
    }

    public async Task ConfirmDropAsync()
    {
        if (Dropping is null) return;
        try
        {
            await _svc.DropProjectTableAsync(ProjectId, Dropping);
            Dropping = null;
            await RefreshTablesAsync();
        }
        catch (Exception ex) { Error = Trim(ex.Message); NotifyStateChanged(); }
    }

    // ── Export ────────────────────────────────────────────────────────────────────────────────
    public async Task ExportAsync(string table, Func<string, string, Task> download)
    {
        try
        {
            var csv = await _svc.ExportProjectTableCsvAsync(ProjectId, table);
            var uri = "data:text/csv;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(csv));
            await download($"{table}.csv", uri);
        }
        catch (Exception ex) { Error = Trim(ex.Message); NotifyStateChanged(); }
    }

    public string ResultCsvUri()
    {
        if (Result is null) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", Result.Columns.Select(CsvEscape)));
        foreach (var row in Result.Rows)
            sb.AppendLine(string.Join(",", row.Select(v => CsvEscape(v ?? ""))));
        return "data:text/csv;charset=utf-8;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
    }

}
