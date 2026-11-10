using PlaceContext.Application.Access.Models;
using PlaceContext.Application.ProjectData;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool ShowQueryDataEditor { get; private set; }
    public string QueryDataSql { get; set; } = "";
    public string QueryDataOutputField { get; set; } = "query_data";
    public string? QueryDataError { get; private set; }
    private int _queryDataStageIndex;
    private bool _editingQueryData;

    // ── Query studio state ───────────────────────────────────────────────────────────────────
    // Tables and columns are loaded for this view model's ProjectId only. Every helper below
    // resolves names against that schema before emitting SQL, so generated statements cannot name
    // another project's objects. Previews go through the project-scoped QueryProjectDataAsync,
    // which runs read-only with the project's role and a pinned search_path.
    public int QueryStudioVersion { get; private set; }
    public IReadOnlyList<ProjectTableSchema> QueryDataTables { get; private set; } = [];
    public string QueryDataTableFilter { get; set; } = "";
    public bool QueryDataSchemaLoading { get; private set; }
    public bool QueryDataSchemaReady { get; private set; }
    public string? QueryDataSchemaError { get; private set; }
    public string? QueryDataTableName { get; private set; }
    public ProjectTableSchema? QueryDataTable =>
        QueryDataTables.FirstOrDefault(table => table.Name == QueryDataTableName);
    public bool QueryDataCanPreview { get; private set; } = true;
    public HashSet<string> QueryDataSelectedColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<QueryDataFilter> QueryDataFilters { get; } = [];
    public string QueryDataPane { get; private set; } = "values";
    public string? QueryDataHelperError { get; private set; }

    public ProjectTablePageResult? QueryDataValues { get; private set; }
    public bool QueryDataValuesLoading { get; private set; }
    public string? QueryDataValuesError { get; private set; }
    public string QueryDataValuesSearch { get; set; } = "";
    public int QueryDataValuesPage { get; private set; } = 1;
    public bool QueryDataHasNextPage =>
        QueryDataValues is not null && QueryDataValues.Rows.Count > WorkflowSql.SampleRows;

    public ProjectQueryResult? QueryDataPreview { get; private set; }
    public bool QueryDataPreviewLoading { get; private set; }
    public string? QueryDataPreviewError { get; private set; }
    public string? QueryDataPreviewWarning { get; private set; }

    public IEnumerable<ProjectTableSchema> FilteredQueryDataTables
    {
        get
        {
            var filter = QueryDataTableFilter?.Trim();
            if (string.IsNullOrEmpty(filter))
            {
                return QueryDataTables;
            }
            return QueryDataTables.Where(table =>
                table.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || table.Columns.Any(column =>
                    column.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public void OpenQueryDataAction(int stageIndex, bool editing = false)
    {
        _queryDataStageIndex = Math.Clamp(stageIndex, 0, EdStages.Count); _editingQueryData = editing;
        var action = editing ? EdStageActions.GetValueOrDefault(stageIndex) as QueryDataChainAction : null;
        QueryDataSql = action?.Sql ?? ""; QueryDataOutputField = action?.OutputField ?? "query_data";
        QueryDataError = null;
        ResetQueryStudio();
        QueryStudioVersion++;
        ShowQueryDataEditor = true; NotifyStateChanged();
    }

    public void CloseQueryDataEditor() { ShowQueryDataEditor = false; NotifyStateChanged(); }

    public void SaveQueryDataAction()
    {
        try
        {
            var action = new QueryDataChainAction(QueryDataSql, QueryDataOutputField);
            if (!_editingQueryData)
            {
                ShiftGatesForInsert(_queryDataStageIndex); ShiftActionsForInsert(_queryDataStageIndex); EdStages.Insert(_queryDataStageIndex, []);
            }
            EdStageActions[_queryDataStageIndex] = action; ShowQueryDataEditor = false; QueryDataError = null;
        }
        catch (ArgumentException ex) { QueryDataError = ex.Message; }
        NotifyStateChanged();
    }

    private void ResetQueryStudio()
    {
        QueryDataTables = [];
        QueryDataTableFilter = "";
        QueryDataSchemaLoading = false;
        QueryDataSchemaReady = false;
        QueryDataSchemaError = null;
        QueryDataTableName = null;
        QueryDataCanPreview = true;
        QueryDataSelectedColumns.Clear();
        QueryDataFilters.Clear();
        QueryDataPane = "values";
        QueryDataHelperError = null;
        QueryDataValues = null;
        QueryDataValuesLoading = false;
        QueryDataValuesError = null;
        QueryDataValuesSearch = "";
        QueryDataValuesPage = 1;
        QueryDataPreview = null;
        QueryDataPreviewLoading = false;
        QueryDataPreviewError = null;
        QueryDataPreviewWarning = null;
    }

    /// <summary>Loads this project's table schema and the first values page. Called once when the
    /// studio opens; failures are shown in the sidebar instead of breaking the dialog.</summary>
    public async Task LoadQueryStudioAsync()
    {
        if (QueryDataSchemaLoading)
        {
            return;
        }
        if (_svc is null)
        {
            QueryDataSchemaReady = true;
            QueryDataSchemaError = "Project tables are unavailable in this context.";
            NotifyStateChanged();
            return;
        }

        QueryDataSchemaLoading = true;
        QueryDataSchemaReady = false;
        QueryDataSchemaError = null;
        NotifyStateChanged();
        try
        {
            QueryDataCanPreview = _permissions is null || await _permissions.HasAsync(Permission.DataRead);
        }
        catch
        {
            QueryDataCanPreview = false;
        }

        try
        {
            QueryDataTables = await _svc.ListProjectTableSchemasAsync(ProjectId);
            QueryDataSchemaReady = true;

            var table = WorkflowSql.ReferencedTables(QueryDataSql, QueryDataTables).FirstOrDefault()
                ?? (string.IsNullOrWhiteSpace(QueryDataSql) ? QueryDataTables.FirstOrDefault() : null)
                ?? QueryDataTables.FirstOrDefault();
            if (table is not null)
            {
                await SelectQueryTableAsync(table.Name);
            }
            else
            {
                QueryDataSchemaError = "This project has no tables yet. Import data or create a table first.";
            }
        }
        catch (Exception ex)
        {
            QueryDataSchemaError = DescribeQueryStudioError(ex);
        }
        finally
        {
            QueryDataSchemaLoading = false;
            NotifyStateChanged();
        }
    }

    public async Task SelectQueryTableAsync(string tableName)
    {
        var table = QueryDataTables.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (table is null)
        {
            return;
        }

        QueryDataTableName = table.Name;
        QueryDataTableFilter = "";
        QueryDataFilters.Clear();
        QueryDataHelperError = null;
        QueryDataSelectedColumns.Clear();
        foreach (var column in table.Columns)
        {
            QueryDataSelectedColumns.Add(column.Name);
        }

        QueryDataValuesPage = 1;
        QueryDataValuesSearch = "";
        // A brand-new step gets a useful, bounded starting point. Existing SQL is never overwritten.
        if (string.IsNullOrWhiteSpace(QueryDataSql))
        {
            QueryDataSql = WorkflowSql.BuildSelect(
                table,
                table.Columns.Select(column => column.Name).ToList(),
                [],
                WorkflowSql.DefaultRowLimit);
        }

        await LoadQueryValuesAsync();
        NotifyStateChanged();
    }

    public async Task LoadQueryValuesAsync(int page = 1)
    {
        if (QueryDataTable is not { } table)
        {
            return;
        }
        if (_svc is null)
        {
            QueryDataValuesError = "Table values are unavailable in this context.";
            NotifyStateChanged();
            return;
        }

        QueryDataValuesPage = Math.Max(1, page);
        QueryDataValuesLoading = true;
        QueryDataValuesError = null;
        NotifyStateChanged();
        try
        {
            var search = string.IsNullOrWhiteSpace(QueryDataValuesSearch) ? null : QueryDataValuesSearch.Trim();
            QueryDataValues = await _svc.QueryProjectTablePageAsync(
                ProjectId,
                table.Name,
                search,
                QueryDataValuesPage,
                WorkflowSql.SampleRows,
                includeTotalCount: false);
        }
        catch (Exception ex)
        {
            QueryDataValues = null;
            QueryDataValuesError = DescribeQueryStudioError(ex);
        }
        finally
        {
            QueryDataValuesLoading = false;
            NotifyStateChanged();
        }
    }

    public Task PreviousQueryValuesPageAsync() =>
        QueryDataValuesPage <= 1 ? Task.CompletedTask : LoadQueryValuesAsync(QueryDataValuesPage - 1);

    public Task NextQueryValuesPageAsync() =>
        QueryDataHasNextPage ? LoadQueryValuesAsync(QueryDataValuesPage + 1) : Task.CompletedTask;

    public Task SearchQueryValuesAsync() => LoadQueryValuesAsync(1);

    public void SetQueryColumn(string columnName, bool selected)
    {
        if (QueryDataTable is not { } table)
        {
            return;
        }
        string canonical;
        try
        {
            canonical = WorkflowSql.Column(table, columnName).Name;
        }
        catch (ArgumentException ex)
        {
            QueryDataHelperError = ex.Message;
            NotifyStateChanged();
            return;
        }

        if (selected)
        {
            QueryDataSelectedColumns.Add(canonical);
        }
        else
        {
            QueryDataSelectedColumns.Remove(canonical);
        }
        NotifyStateChanged();
    }

    public void SetAllQueryColumns(bool selected)
    {
        if (QueryDataTable is not { } table)
        {
            return;
        }
        QueryDataSelectedColumns.Clear();
        if (selected)
        {
            foreach (var column in table.Columns)
            {
                QueryDataSelectedColumns.Add(column.Name);
            }
        }
        NotifyStateChanged();
    }

    /// <summary>Adds (or replaces) a filter for a real column on the selected project table.
    /// Null adds an IS NULL condition; every other value is type-checked against the schema.</summary>
    public void AddQueryFilter(string columnName, string? value)
    {
        if (QueryDataTable is not { } table)
        {
            return;
        }
        try
        {
            var column = WorkflowSql.Column(table, columnName);
            var filter = new QueryDataFilter(column.Name, column.Type, value);
            var existing = QueryDataFilters.FindIndex(candidate =>
                string.Equals(candidate.Column, filter.Column, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                QueryDataFilters[existing] = filter;
            }
            else
            {
                QueryDataFilters.Add(filter);
            }
            QueryDataHelperError = null;
            QueryDataPane = "values";
        }
        catch (ArgumentException ex)
        {
            QueryDataHelperError = ex.Message;
        }
        NotifyStateChanged();
    }

    public void RemoveQueryFilter(QueryDataFilter filter)
    {
        QueryDataFilters.Remove(filter);
        NotifyStateChanged();
    }

    public string? BuildSelectedQuery()
    {
        if (QueryDataTable is not { } table)
        {
            QueryDataHelperError = "Choose a project table first.";
            NotifyStateChanged();
            return null;
        }
        try
        {
            var columns = table.Columns
                .Where(column => QueryDataSelectedColumns.Contains(column.Name))
                .Select(column => column.Name)
                .ToList();
            var sql = WorkflowSql.BuildSelect(table, columns, QueryDataFilters.ToList());
            QueryDataHelperError = null;
            NotifyStateChanged();
            return sql;
        }
        catch (ArgumentException ex)
        {
            QueryDataHelperError = ex.Message;
            NotifyStateChanged();
            return null;
        }
    }

    public string? BuildCountQuery()
    {
        if (QueryDataTable is not { } table)
        {
            QueryDataHelperError = "Choose a project table first.";
            NotifyStateChanged();
            return null;
        }
        try
        {
            var sql = WorkflowSql.BuildCount(table, QueryDataFilters.ToList());
            QueryDataHelperError = null;
            NotifyStateChanged();
            return sql;
        }
        catch (ArgumentException ex)
        {
            QueryDataHelperError = ex.Message;
            NotifyStateChanged();
            return null;
        }
    }

    public async Task PreviewQueryDataAsync(string sql)
    {
        QueryDataPane = "result";
        QueryDataPreviewError = null;
        QueryDataPreview = null;
        QueryDataPreviewWarning = null;
        if (string.IsNullOrWhiteSpace(sql))
        {
            QueryDataPreviewError = "Write or generate a query first.";
            NotifyStateChanged();
            return;
        }
        if (_svc is null)
        {
            QueryDataPreviewError = "Preview is unavailable in this context.";
            NotifyStateChanged();
            return;
        }

        QueryDataSql = sql;
        QueryDataPreviewLoading = true;
        QueryDataHelperError = null;
        NotifyStateChanged();
        try
        {
            // Project-scoped by design: the reader executes in a read-only transaction as the
            // project's role with search_path pinned to its private schema.
            var result = await _svc.QueryProjectDataAsync(ProjectId, sql);
            QueryDataPreview = result;
            QueryDataPreviewWarning = WorkflowSql.PreviewWarning(result);
            var unknown = WorkflowSql.UnknownReferencedTables(sql, QueryDataTables);
            if (unknown.Count > 0)
            {
                QueryDataHelperError =
                    $"Not in this project's schema: {string.Join(", ", unknown)}. "
                    + "The isolated preview role can only read this project's tables.";
            }
        }
        catch (Exception ex)
        {
            QueryDataPreviewError = DescribeQueryStudioError(ex);
        }
        finally
        {
            QueryDataPreviewLoading = false;
            NotifyStateChanged();
        }
    }

    public void UseSelectedTableAsOutputField()
    {
        if (QueryDataTable is { } table)
        {
            QueryDataOutputField = table.Name;
            NotifyStateChanged();
        }
    }

    /// <summary>Supplied by the page so clicking a column in the schema sidebar can insert text at
    /// the active editor's caret without the ViewModel depending on a component reference.</summary>
    public Func<string, Task>? InsertQueryText { get; set; }

    public Task InsertQueryColumnAsync(string text) =>
        InsertQueryText is null ? Task.CompletedTask : InsertQueryText(text);

    public void ShowQueryPane(string pane)
    {
        QueryDataPane = pane == "result" ? "result" : "values";
        NotifyStateChanged();
    }

    public void ReportQueryHelperError(string message)
    {
        QueryDataHelperError = string.IsNullOrWhiteSpace(message) ? null : message;
        NotifyStateChanged();
    }

    private static string DescribeQueryStudioError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "You need data-read permission to preview project data.",
        _ => ex.Message,
    };
}
