using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ProjectDataViewModel
{
    private readonly List<ProjectDataTableTab> _openTabs = [];
    private bool _loadingIndices;
    private string? _activeTableName;
    private string? _lastChartJson;
    private static readonly TimeSpan MaterializeIndexRefreshDelay = TimeSpan.FromMilliseconds(450);
    private const int MaterializeIndexRefreshAttempts = 6;

    public IReadOnlyList<ProjectDataTableTab> OpenTabs => _openTabs;
    public ProjectDataTableTab? ActiveTableTab =>
        _openTabs.FirstOrDefault(tab => tab.TableName == _activeTableName);
    public string? ActiveTableName => _activeTableName;
    public string? StudioJsonViewerTitle { get; private set; }
    public string? StudioJsonViewerValue { get; private set; }
    public string TableFilter { get; set; } = "";
    public string ResultFilter { get; set; } = "";
    public ProjectDataResultsPane ResultsPane { get; private set; } =
        ProjectDataResultsPane.Table;
    public int? SelectedRowIndex { get; private set; }

    public IReadOnlyList<ProjectTableInfo> FilteredTables =>
        string.IsNullOrWhiteSpace(TableFilter)
            ? Tables.ToList()
            : Tables
                .Where(table =>
                    table.Name.Contains(TableFilter, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

    public IReadOnlyList<OpenSearchIndexView> FilteredIndices =>
        string.IsNullOrWhiteSpace(TableFilter)
            ? Indices.ToList()
            : Indices
                .Where(index =>
                    index.Name.Contains(TableFilter, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

    public IReadOnlyList<SavedQueryRecord> FilteredSavedQueries =>
        string.IsNullOrWhiteSpace(TableFilter)
            ? SavedQueries.ToList()
            : SavedQueries
                .Where(query =>
                    query.Name.Contains(TableFilter, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();

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

    public List<(int Index, IReadOnlyList<string?> Row)> FilteredResultRows(
        ProjectDataTableTab tab
    )
    {
        if (tab.Result is not { Rows: { } rows })
            return [];

        var indexed = rows.Select((row, index) => (Index: index, Row: row)).ToList();
        var filter = ResultFilter.Trim();
        if (filter.Length == 0)
            return indexed;

        return indexed
            .Where(item =>
                (item.Index + 1)
                    .ToString()
                    .Contains(filter, StringComparison.OrdinalIgnoreCase)
                || item.Row.Any(cell =>
                    cell is not null
                    && cell.Contains(filter, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();
    }

    public string TableTag(ProjectTableInfo table) =>
        table.IsView ? "view" : table.ReadOnly ? "system" : "table";

    public string TableEngineTag(ProjectTableInfo table) =>
        table.IsView ? "VIEW" : table.ReadOnly ? "SYS" : "MT";

    public string SqlPreview(string sql)
    {
        var singleLine = sql.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length > 60 ? singleLine[..60] + "…" : singleLine;
    }

    public string FormatElapsed(ProjectQueryResult _) =>
        "0.003s";

    public string FormatDocumentCount(long count) => count.ToString("N0");

    public ProjectDataChartSpec? BuildChartSpec(ProjectDataTableTab tab)
    {
        if (tab.Result is not { Columns.Count: > 1 } result || result.Rows.Count == 0)
            return null;

        var labels = new List<string>();
        var seriesNames = new List<string>();
        var seriesValues = new List<List<double>>();
        for (var columnIndex = 1; columnIndex < result.Columns.Count; columnIndex++)
        {
            var values = new List<double?>(result.Rows.Count);
            var hasNumber = false;
            foreach (var row in result.Rows)
            {
                var raw = columnIndex < row.Count ? row[columnIndex] : null;
                if (
                    double.TryParse(
                        raw,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var value
                    )
                )
                {
                    values.Add(value);
                    hasNumber = true;
                }
                else
                {
                    values.Add(null);
                }
            }

            if (!hasNumber)
                continue;

            seriesNames.Add(result.Columns[columnIndex]);
            seriesValues.Add(values.Select(value => value ?? 0).ToList());
        }

        if (seriesNames.Count == 0)
            return null;

        var rowCount = Math.Min(result.Rows.Count, 200);
        foreach (var row in result.Rows.Take(rowCount))
            labels.Add(row.Count > 0 ? row[0] ?? "" : "");

        var chart = new
        {
            type = "bar",
            title = $"{tab.TableName} — results",
            labels,
            series = seriesNames
                .Select((name, index) => new
                {
                    name,
                    values = seriesValues[index].Take(rowCount).ToList(),
                })
                .ToList(),
        };
        return new ProjectDataChartSpec(
            chart.title,
            $"{rowCount} row(s) · {seriesNames.Count} series",
            JsonSerializer.Serialize(chart)
        );
    }

    public void SelectRow(int index)
    {
        SelectedRowIndex = SelectedRowIndex == index ? null : index;
    }

    public void ShowResultsPane(ProjectDataResultsPane pane)
    {
        ResultsPane = pane;
        _lastChartJson = null;
        NotifyStateChanged();
    }

    public async Task AfterStudioRenderAsync()
    {
        await AfterRenderAsync();
        if (ActiveTableTab is { } active)
        {
            await ShowSqlTabAsync(active.TableName, active.Sql);
            await _js.InvokeVoidAsync("pcstudio.splitter", "pcdata-studio-splitter");
        }

        if (
            ResultsPane == ProjectDataResultsPane.Chart
            && ActiveTableTab is { } chartTab
            && BuildChartSpec(chartTab) is { } spec
        )
        {
            var chartKey = $"{chartTab.TableName}|{spec.Json}";
            if (!string.Equals(_lastChartJson, chartKey, StringComparison.Ordinal))
            {
                _lastChartJson = chartKey;
                await _js.InvokeVoidAsync(
                    "pcchart.render",
                    "pcdata-chart-canvas",
                    spec.Json
                );
            }
        }
    }

    public async Task OpenTableTabAsync(string tableName)
    {
        var existing = _openTabs.FirstOrDefault(tab =>
            StringComparer.Ordinal.Equals(tab.TableName, tableName)
        );
        if (existing is not null)
        {
            _activeTableName = existing.TableName;
            await ShowSqlTabAsync(existing.TableName, existing.Sql);
            if (existing.Result is null && !existing.Running && existing.Error is null)
                await RunTableTabAsync(existing);
            return;
        }

        var tab = new ProjectDataTableTab(tableName, DefaultSqlFor(tableName));
        _openTabs.Add(tab);
        _activeTableName = tableName;
        await ShowSqlTabAsync(tableName, tab.Sql);
        await RunTableTabAsync(tab);
    }

    public async Task OpenIndexTabAsync(string indexName)
    {
        var existing = _openTabs.FirstOrDefault(tab =>
            tab.IsIndex && StringComparer.Ordinal.Equals(tab.TableName, indexName)
        );
        if (existing is not null)
        {
            _activeTableName = existing.TableName;
            await ShowSqlTabAsync(existing.TableName, existing.Sql);
            if (existing.Result is null && !existing.Running && existing.Error is null)
                await RunTableTabAsync(existing);
            return;
        }

        var tab = new ProjectDataTableTab(
            indexName,
            DefaultOpenSearchSqlFor(indexName),
            ProjectDataTableTabSource.OpenSearch
        );
        _openTabs.Add(tab);
        _activeTableName = indexName;
        await ShowSqlTabAsync(indexName, tab.Sql);
        await RunTableTabAsync(tab);
    }

    public async Task OpenSavedQueryAsync(SavedQueryRecord query)
    {
        var existing = _openTabs.FirstOrDefault(tab =>
            StringComparer.Ordinal.Equals(tab.TableName, query.Name)
        );
        if (existing is not null)
        {
            _activeTableName = existing.TableName;
            existing.Sql = query.Sql;
            await ShowSqlTabAsync(existing.TableName, existing.Sql);
            await RunTableTabAsync(existing);
            return;
        }

        var tab = new ProjectDataTableTab(query.Name, query.Sql);
        _openTabs.Add(tab);
        _activeTableName = query.Name;
        await ShowSqlTabAsync(query.Name, tab.Sql);
        await RunTableTabAsync(tab);
    }

    public void SaveCurrentQuery() =>
        OpenSaveQueryDialog(ActiveTableTab?.TableName ?? "untitled-query");

    public async Task SaveQueryFromEditorAsync() =>
        await SaveQueryAsync(ReadEditorForSaveAsync);

    public async Task ActivateTabAsync(string tableName)
    {
        if (StringComparer.Ordinal.Equals(tableName, _activeTableName))
            return;

        if (ActiveTableTab is { } current)
        {
            var value = await GetSqlEditorValueAsync();
            if (value is not null)
                current.Sql = value;
        }

        _activeTableName = tableName;
        await ShowSqlTabAsync(
            tableName,
            ActiveTableTab?.Sql ?? DefaultSqlFor(tableName)
        );
        NotifyStateChanged();
    }

    public async Task CloseTableTabAsync(string tableName)
    {
        var index = _openTabs.FindIndex(tab =>
            StringComparer.Ordinal.Equals(tab.TableName, tableName)
        );
        if (index < 0)
            return;

        var wasActive = _activeTableName == tableName;
        if (wasActive)
        {
            _activeTableName = _openTabs.Count == 1
                ? null
                : _openTabs[index > 0 ? index - 1 : 1].TableName;
        }

        _openTabs.RemoveAt(index);
        await CloseSqlEditorFileAsync(tableName);

        if (wasActive && _activeTableName is not null)
        {
            var tab = _openTabs.FirstOrDefault(item =>
                StringComparer.Ordinal.Equals(item.TableName, _activeTableName)
            );
            await ShowSqlTabAsync(
                _activeTableName,
                tab?.Sql ?? DefaultSqlFor(_activeTableName)
            );
        }
        else if (_openTabs.Count == 0)
        {
            ResetSqlEditor();
        }

        NotifyStateChanged();
    }

    public async Task ResetTableTabSqlAsync(ProjectDataTableTab tab)
    {
        tab.Sql = tab.IsIndex
            ? DefaultOpenSearchSqlFor(tab.TableName)
            : DefaultSqlFor(tab.TableName);
        tab.Result = null;
        tab.Error = null;
        await SetSqlEditorValueAsync(tab.Sql);
        NotifyStateChanged();
    }

    public async Task RunActiveTableTabAsync()
    {
        if (ActiveTableTab is { } tab)
            await RunTableTabAsync(tab);
    }

    public async Task RunTableTabAsync(ProjectDataTableTab tab)
    {
        tab.Running = true;
        tab.Error = null;
        NotifyStateChanged();
        try
        {
            var editorSql = await GetSqlEditorValueAsync();
            if (editorSql is not null)
                tab.Sql = editorSql;
            tab.Result = tab.IsIndex
                ? await _svc.SearchOpenSearchSqlAsync(ProjectId, tab.Sql)
                : await _svc.ExecuteProjectDataAsync(ProjectId, tab.Sql);
        }
        catch (Exception ex)
        {
            tab.Result = null;
            tab.Error = Trim(ex.Message);
        }
        finally
        {
            tab.Running = false;
            NotifyStateChanged();
        }
    }

    public string GetTabResultCsvUri(ProjectDataTableTab tab)
    {
        if (tab.Result is null)
            return "";

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", tab.Result.Columns.Select(CsvEscape)));
        foreach (var row in tab.Result.Rows)
            csv.AppendLine(string.Join(",", row.Select(value => CsvEscape(value ?? ""))));
        return "data:text/csv;charset=utf-8;base64,"
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(csv.ToString()));
    }

    public void OpenStudioJsonViewer(string column, string value)
    {
        using var document = JsonDocument.Parse(value);
        StudioJsonViewerTitle = column;
        StudioJsonViewerValue = JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions { WriteIndented = true }
        );
        NotifyStateChanged();
    }

    public void CloseStudioJsonViewer()
    {
        StudioJsonViewerTitle = null;
        StudioJsonViewerValue = null;
        NotifyStateChanged();
    }

    public void PruneOpenTabs()
    {
        var existing = Tables.Select(table => table.Name).ToHashSet(StringComparer.Ordinal);
        _openTabs.RemoveAll(tab =>
            !tab.IsIndex && !existing.Contains(tab.TableName)
        );

        if (
            _activeTableName is not null
            && _openTabs.All(tab =>
                !StringComparer.Ordinal.Equals(tab.TableName, _activeTableName)
            )
        )
        {
            _activeTableName = _openTabs.FirstOrDefault()?.TableName;
        }
    }

    private async Task<string?> ReadEditorForSaveAsync(string _)
    {
        var editorValue = await GetSqlEditorValueAsync();
        return editorValue ?? ActiveTableTab?.Sql;
    }

    private static string DefaultSqlFor(string tableName) =>
        $"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT 100;";

    private static string DefaultOpenSearchSqlFor(string indexName) =>
        $"SELECT * FROM `{indexName.Replace("`", "``")}` LIMIT 100;";
}
