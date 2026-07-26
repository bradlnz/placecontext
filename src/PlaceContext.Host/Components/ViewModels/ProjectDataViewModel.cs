using PlaceContext.Application;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class ProjectDataViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;

    public ProjectDataViewModel(IPlaceContextService svc) => _svc = svc;

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    // ── Tables ────────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<ProjectTableInfo> Tables { get; private set; } = Array.Empty<ProjectTableInfo>();
    public bool TablesReady { get; private set; }

    // ── SQL editor ────────────────────────────────────────────────────────────────────────────
    public ProjectQueryResult? Result { get; private set; }
    public string? Error { get; private set; }
    public bool Running { get; private set; }
    public bool MonacoReady { get; set; }
    public bool MonacoLite { get; set; }
    public const string EditorId = "pcdata-editor";
    public const string StarterSql =
        "-- This project's own database. Standard SQL — a few ideas:\n" +
        "--   CREATE TABLE readings (at timestamptz DEFAULT now(), sensor text, value numeric);\n" +
        "--   INSERT INTO readings (sensor, value) VALUES ('door', 21.5);\n" +
        "--   SELECT sensor, avg(value) FROM readings GROUP BY sensor;\n\n";

    // ── New table wizard ──────────────────────────────────────────────────────────────────────
    public sealed class ColumnDraft { public string Name = ""; public string Type = "text"; public bool NotNull; public bool PrimaryKey; }
    public sealed class TableDraft { public string Name = ""; public List<ColumnDraft> Columns = new(); }
    public static readonly string[] ColumnTypes = { "text", "integer", "bigint", "numeric", "boolean", "timestamptz", "date", "uuid", "jsonb" };

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

    // ── Edit table ────────────────────────────────────────────────────────────────────────────
    public string? EditTable { get; private set; }
    public IReadOnlyList<ProjectColumnInfo> EditColumns { get; private set; } = Array.Empty<ProjectColumnInfo>();
    public string? EditError { get; private set; }
    public string? ConfirmDropColumn { get; set; }
    public ColumnDraft AddCol { get; set; } = new();
    public bool AlteringColumn { get; private set; }

    // ── Rename / drop ─────────────────────────────────────────────────────────────────────────
    public string? Renaming { get; private set; }
    public string? RenameTo { get; set; }
    public string? Dropping { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId) => ProjectId = projectId;

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
        NewTable = new TableDraft { Columns = { new ColumnDraft { Name = "id", Type = "uuid", NotNull = true, PrimaryKey = true } } };
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

    // ── Edit table ────────────────────────────────────────────────────────────────────────────
    public async Task StartEditAsync(string table)
    {
        EditTable = table;
        EditError = null;
        ConfirmDropColumn = null;
        AddCol = new ColumnDraft();
        await RefreshColumnsAsync();
        NotifyStateChanged();
    }

    public void CloseEdit() { EditTable = null; NotifyStateChanged(); }

    private async Task RefreshColumnsAsync()
    {
        if (EditTable is null) return;
        try { EditColumns = await _svc.ListProjectTableColumnsAsync(ProjectId, EditTable); }
        catch (Exception ex) { EditError = Trim(ex.Message); }
    }

    public async Task AddColumnToTableAsync()
    {
        if (EditTable is null) return;
        EditError = null;
        if (string.IsNullOrWhiteSpace(AddCol.Name)) { EditError = "Give the column a name."; NotifyStateChanged(); return; }
        AlteringColumn = true;
        try
        {
            await _svc.AddProjectTableColumnAsync(ProjectId, EditTable,
                new ProjectColumnSpec(AddCol.Name.Trim(), AddCol.Type, AddCol.NotNull, PrimaryKey: false));
            AddCol = new ColumnDraft();
            await RefreshColumnsAsync();
            await RefreshTablesAsync();
        }
        catch (Exception ex) { EditError = Trim(ex.Message); }
        finally { AlteringColumn = false; NotifyStateChanged(); }
    }

    public async Task DropColumnAsync(string column)
    {
        if (EditTable is null) return;
        EditError = null;
        ConfirmDropColumn = null;
        try
        {
            await _svc.DropProjectTableColumnAsync(ProjectId, EditTable, column);
            await RefreshColumnsAsync();
        }
        catch (Exception ex) { EditError = Trim(ex.Message); }
        NotifyStateChanged();
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

    // ── Utilities ─────────────────────────────────────────────────────────────────────────────
    public static List<string[]> ParseCsvRecords(string text)
    {
        var records = new List<string[]>();
        var field = new System.Text.StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else
            {
                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(field.ToString()); field.Clear(); break;
                    case '\r': break;
                    case '\n':
                        row.Add(field.ToString()); field.Clear();
                        records.Add(row.ToArray()); row = new List<string>();
                        break;
                    default: field.Append(c); break;
                }
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            records.Add(row.ToArray());
        }
        return records;
    }

    public static string InferType(IEnumerable<string?> values)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sample = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList();
        if (sample.Count == 0) return "text";
        bool All(Func<string, bool> f) => sample.All(f);
        if (All(v => v is "true" or "false" or "TRUE" or "FALSE" or "True" or "False")) return "boolean";
        if (All(v => long.TryParse(v, System.Globalization.NumberStyles.Integer, ci, out _))) return "bigint";
        if (All(v => decimal.TryParse(v, System.Globalization.NumberStyles.Number, ci, out _))) return "numeric";
        if (All(v => Guid.TryParse(v, out _))) return "uuid";
        if (All(v => DateTime.TryParse(v, ci, System.Globalization.DateTimeStyles.None, out _))) return "timestamptz";
        return "text";
    }

    public static string SanitizeIdent(string raw, int ordinal)
    {
        var lowered = (raw ?? "").Trim().ToLowerInvariant();
        var chars = lowered.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var s = System.Text.RegularExpressions.Regex.Replace(new string(chars), "_+", "_").Trim('_');
        if (string.IsNullOrEmpty(s)) return $"col{ordinal + 1}";
        if (!char.IsLetter(s[0]) && s[0] != '_') s = "c_" + s;
        return s.Length > 63 ? s[..63] : s;
    }

    public static string CsvEscape(string s) =>
        s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static string Trim(string message)
        => message.Length > 400 ? message[..400] + "…" : message;
}
