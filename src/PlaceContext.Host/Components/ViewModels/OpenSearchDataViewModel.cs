using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class OpenSearchDataViewModel : PageViewModel, IDisposable
{
    private readonly IPlaceContextService Svc;
    private readonly PortalUiState Ui;
    private readonly IJSRuntime JS;
    private readonly IPermissionService Permissions;

    public OpenSearchDataViewModel(
        IPlaceContextService svc,
        PortalUiState ui,
        IJSRuntime jS,
        IPermissionService permissions
    )
    {
        Svc = svc;
        Ui = ui;
        JS = jS;
        Permissions = permissions;
    }

    [Parameter]
    public Guid ProjectId { get; set; }

    [SupplyParameterFromQuery(Name = "index")]
    public string? RequestedIndex { get; set; }

    [SupplyParameterFromQuery(Name = "q")]
    public string? RequestedQuery { get; set; }

    [SupplyParameterFromQuery(Name = "document")]
    public string? RequestedDocument { get; set; }

    public const string PreviewCanvasId = "opensearch-chart-preview";

    public IReadOnlyList<OpenSearchIndexView> Indices = Array.Empty<OpenSearchIndexView>();
    public IReadOnlyList<OpenSearchFieldView> Fields = Array.Empty<OpenSearchFieldView>();
    public List<OpenSearchDashboardView> Dashboards = new();
    public OpenSearchSearchView? Result;
    public string IndexPattern = "*";
    public string QueryText = "";
    public string BucketField = "";
    public string BucketType = "terms";
    public string ChartType = "bar";
    public string MetricType = "count";
    public OpenSearchMetricMode MetricMode => OpenSearchPresentationCatalog.ParseMetric(MetricType);
    public OpenSearchBucketMode BucketMode => OpenSearchPresentationCatalog.ParseBucket(BucketType);
    public bool RequiresMetricField => MetricMode != OpenSearchMetricMode.Count;
    public bool UsesDateBuckets => BucketMode == OpenSearchBucketMode.DateHistogram;

    public void SetMetricMode(ChangeEventArgs args) =>
        MetricType = OpenSearchPresentationCatalog.MetricKey(
            OpenSearchPresentationCatalog.ParseMetric(args.Value?.ToString() ?? "")
        );

    public string MetricField = "";
    public string DateInterval = "day";
    public string DashboardName = "";
    public string? Error;
    public bool Loading;
    public bool InitialLoadComplete;
    public bool Searching;
    public bool Syncing;
    public bool CanSync;
    public string? SyncMessage;
    public bool SavingDashboard;
    public bool GeneratingCharts;
    public Guid? EditingDashboardId;
    public Guid? Refreshing;
    public Guid? Replicating;
    public int Page = 1;
    public int PageSize { get; } = 25;
    public string? LastPreviewSpec;
    public readonly Dictionary<Guid, DateTimeOffset> RenderedDashboards = new();
    public readonly HashSet<string> RenderedGeneratedCharts = new();
    public List<GeneratedOpenSearchChart> GeneratedCharts = new();
    public OpenSearchLastUpdatedView? LastUpdated;

    public IEnumerable<OpenSearchFieldView> AggregatableFields =>
        Fields.Where(item => item.Aggregatable);
    public IEnumerable<OpenSearchFieldView> NumericFields =>
        Fields.Where(item =>
            item.Aggregatable
            && item.Type
                is "byte"
                    or "short"
                    or "integer"
                    or "long"
                    or "half_float"
                    or "float"
                    or "double"
                    or "scaled_float"
        );
    public IReadOnlyList<string> VisibleColumns =>
        Result is null
            ? Array.Empty<string>()
            : Result.Hits.SelectMany(hit => hit.Fields.Keys).Distinct().Take(10).ToList();

    public Task AttachAndInitializeAsync(Func<Task> stateChanged)
    {
        Attach(stateChanged);
        return Task.CompletedTask;
    }

    public async Task SetParametersAsync()
    {
        Ui.Set("Data Search", "OpenSearch · queries · charts");
        Loading = true;
        try
        {
            var indicesTask = Svc.ListOpenSearchIndicesAsync(ProjectId);
            var dashboardsTask = Svc.ListOpenSearchDashboardsAsync(ProjectId);
            var permissionTask = Permissions.HasAsync(Permission.SettingsManage);
            await Task.WhenAll(indicesTask, dashboardsTask, permissionTask);
            Indices = await indicesTask;
            Dashboards = (await dashboardsTask).ToList();
            CanSync = await permissionTask;
            if (Indices.Count > 0)
                IndexPattern =
                    !string.IsNullOrWhiteSpace(RequestedIndex)
                    && Indices.Any(item =>
                        item.Name.Equals(RequestedIndex, StringComparison.Ordinal)
                    )
                        ? RequestedIndex
                        : DefaultIndex(Indices).Name;
            if (!string.IsNullOrWhiteSpace(RequestedQuery))
                QueryText = RequestedQuery;
            await LoadFieldsAsync();
            if (!string.IsNullOrWhiteSpace(RequestedQuery))
                await SearchAsync();
            else
                await GenerateChartsAsync();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
            try
            {
                Dashboards = (await Svc.ListOpenSearchDashboardsAsync(ProjectId)).ToList();
            }
            catch
            {
                Dashboards = new();
            }
        }
        finally
        {
            Loading = false;
            InitialLoadComplete = true;
        }
    }

    public async Task TriggerSyncAsync()
    {
        Syncing = true;
        SyncMessage = null;
        try
        {
            var result = await Svc.TriggerOpenSearchSyncAsync(ProjectId);
            SyncMessage = result.Message;
        }
        catch (Exception ex)
        {
            SyncMessage = ex.Message;
        }
        finally
        {
            Syncing = false;
        }
    }

    public async Task AfterRenderAsync(bool firstRender)
    {
        if (Result?.ChartSpecJson is { } preview && preview != LastPreviewSpec)
        {
            try
            {
                await JS.InvokeVoidAsync("pcchart.render", PreviewCanvasId, preview);
                LastPreviewSpec = preview;
            }
            catch (JSException) { }
        }

        foreach (var dashboard in Dashboards)
        {
            if (
                RenderedDashboards.TryGetValue(dashboard.Id, out var rendered)
                && rendered == dashboard.UpdatedAt
            )
                continue;
            try
            {
                await JS.InvokeVoidAsync(
                    "pcchart.render",
                    CanvasId(dashboard.Id.ToString()),
                    dashboard.ChartSpecJson
                );
                RenderedDashboards[dashboard.Id] = dashboard.UpdatedAt;
            }
            catch (JSException) { }
        }

        foreach (var chart in GeneratedCharts)
        {
            if (RenderedGeneratedCharts.Contains(chart.Id))
                continue;
            try
            {
                await JS.InvokeVoidAsync(
                    "pcchart.render",
                    GeneratedCanvasId(chart.Id),
                    chart.ChartSpecJson
                );
                RenderedGeneratedCharts.Add(chart.Id);
            }
            catch (JSException) { }
        }
    }

    public async Task LoadFieldsAsync()
    {
        if (string.IsNullOrWhiteSpace(IndexPattern))
            return;
        try
        {
            Fields = await Svc.ListOpenSearchFieldsAsync(ProjectId, IndexPattern);
            var dateFields = Fields
                .Where(field => field.Aggregatable && field.Type is "date" or "date_nanos")
                .OrderBy(field => LastUpdatedFieldPriority(field.Name))
                .ThenBy(field => field.Name)
                .Select(field => field.Name)
                .ToList();
            LastUpdated = null;
            try
            {
                LastUpdated = await Svc.GetOpenSearchLastUpdatedAsync(
                    ProjectId,
                    IndexPattern,
                    dateFields
                );
            }
            catch
            {
                // Last-updated metadata is optional; field browsing remains usable if an
                // index has an incompatible date mapping or denies aggregations.
            }
            Error = null;
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
            Fields = Array.Empty<OpenSearchFieldView>();
            LastUpdated = null;
        }
    }

    public string LastUpdatedLabel =>
        LastUpdated?.Value is { } value
            ? value.UtcDateTime.ToString(
                "d MMM yyyy, h:mm tt 'UTC'",
                System.Globalization.CultureInfo.InvariantCulture
            )
            : "Unavailable";

    public string LastUpdatedTitle =>
        LastUpdated?.Field is { } timestampField
            ? $"Newest document value in {timestampField}"
            : "No aggregatable date field is available for this index";

    public static int LastUpdatedFieldPriority(string field)
    {
        var name = field.ToLowerInvariant();
        if (name.Contains("updated") || name.Contains("last_modified"))
            return 0;
        if (name.Contains("modified"))
            return 1;
        if (name.Contains("timestamp"))
            return 2;
        if (name.Contains("created"))
            return 3;
        return 4;
    }

    public async Task SelectIndexAsync(OpenSearchIndexView index)
    {
        IndexPattern = index.Name;
        Page = 1;
        Result = null;
        await LoadFieldsAsync();
        await GenerateChartsAsync();
    }

    public async Task GenerateChartsAsync()
    {
        if (string.IsNullOrWhiteSpace(IndexPattern) || Fields.Count == 0)
            return;
        GeneratingCharts = true;
        GeneratedCharts = new();
        RenderedGeneratedCharts.Clear();
        try
        {
            var candidates = ChartCandidates();
            var generated = await Task.WhenAll(
                candidates.Select(async candidate =>
                {
                    try
                    {
                        var result = await Svc.SearchOpenSearchAsync(
                            new OpenSearchSearchRequest(
                                ProjectId,
                                IndexPattern,
                                QueryText,
                                1,
                                1,
                                candidate.BucketField,
                                candidate.BucketType,
                                candidate.ChartType,
                                candidate.MetricType,
                                candidate.MetricField,
                                candidate.DateInterval
                            )
                        );
                        return result.ChartSpecJson is null
                            ? null
                            : new GeneratedOpenSearchChart(
                                candidate.Id,
                                candidate.Title,
                                candidate.Subtitle,
                                result.ChartSpecJson
                            );
                    }
                    catch
                    {
                        // A sparse or incompatible field should not suppress other useful charts.
                        return null;
                    }
                })
            );
            GeneratedCharts = generated
                .Where(chart => chart is not null)
                .Cast<GeneratedOpenSearchChart>()
                .ToList();
        }
        finally
        {
            GeneratingCharts = false;
        }
    }

    public IReadOnlyList<GeneratedChartCandidate> ChartCandidates()
    {
        var date = Fields
            .Where(field => field.Aggregatable && field.Type == "date")
            .OrderBy(FieldPriority)
            .FirstOrDefault();
        var categories = Fields.Where(IsUsefulCategory).OrderBy(FieldPriority).Take(2).ToList();
        var numerics = Fields
            .Where(IsNumeric)
            .OrderBy(FieldPriority)
            .GroupBy(MetricFamily)
            .Select(group => group.First())
            .Take(2)
            .ToList();

        var candidates = new List<GeneratedChartCandidate>();
        if (date is not null)
        {
            candidates.Add(
                new(
                    $"date-{date.Name}",
                    $"{Humanize(date.Name)} over time",
                    $"Monthly document count by {date.Name}",
                    date.Name,
                    "date_histogram",
                    "line",
                    "count",
                    null,
                    "month"
                )
            );
        }
        if (categories.Count > 0)
        {
            var category = categories[0];
            candidates.Add(
                new(
                    $"terms-{category.Name}",
                    $"Top {Humanize(category.Name)} values",
                    $"Document count by {category.Name}",
                    category.Name,
                    "terms",
                    "bar",
                    "count",
                    null,
                    null
                )
            );
        }
        foreach (var numeric in numerics)
        {
            if (categories.Count > 0)
            {
                var category = categories[0];
                candidates.Add(
                    new(
                        $"avg-{numeric.Name}-by-{category.Name}",
                        $"Average {Humanize(numeric.Name)} by {Humanize(category.Name)}",
                        $"Average {numeric.Name} grouped by {category.Name}",
                        category.Name,
                        "terms",
                        "bar",
                        "avg",
                        numeric.Name,
                        null
                    )
                );
            }
            else if (date is not null)
            {
                candidates.Add(
                    new(
                        $"avg-{numeric.Name}-over-{date.Name}",
                        $"Average {Humanize(numeric.Name)} over time",
                        $"Monthly average of {numeric.Name}",
                        date.Name,
                        "date_histogram",
                        "line",
                        "avg",
                        numeric.Name,
                        "month"
                    )
                );
            }
        }
        if (categories.Count > 1)
        {
            var category = categories[1];
            candidates.Add(
                new(
                    $"terms-{category.Name}",
                    $"Top {Humanize(category.Name)} values",
                    $"Document count by {category.Name}",
                    category.Name,
                    "terms",
                    "bar",
                    "count",
                    null,
                    null
                )
            );
        }
        return candidates.Take(4).ToList();
    }

    public static OpenSearchIndexView DefaultIndex(IReadOnlyList<OpenSearchIndexView> indices) =>
        indices.FirstOrDefault(index =>
            index.DocumentCount > 0
            && index.Name.Contains(
                "property-feasibility-assessments",
                StringComparison.OrdinalIgnoreCase
            )
        )
        ?? indices.FirstOrDefault(index =>
            index.DocumentCount > 0
            && index.Name.Contains("development-applications", StringComparison.OrdinalIgnoreCase)
        )
        ?? indices.OrderByDescending(index => index.DocumentCount).First();

    public static bool IsUsefulCategory(OpenSearchFieldView field)
    {
        if (!field.Aggregatable || field.Type is not ("keyword" or "boolean"))
            return false;
        var name = field.Name.ToLowerInvariant();
        return !new[]
        {
            "id",
            "hash",
            "url",
            "path",
            "geometry",
            "coordinate",
            "description",
            "address",
            "title",
            "raw",
        }.Any(token =>
            name.EndsWith(token) || name.Contains($"_{token}") || name.Contains($".{token}")
        );
    }

    public static bool IsNumeric(OpenSearchFieldView field) =>
        field.Aggregatable
        && field.Type
            is "byte"
                or "short"
                or "integer"
                or "long"
                or "half_float"
                or "float"
                or "double"
                or "scaled_float";

    public static string MetricFamily(OpenSearchFieldView field)
    {
        var name = field.Name.ToLowerInvariant();
        string[] families =
        [
            "profit",
            "cashflow",
            "margin",
            "yield",
            "cost",
            "rent",
            "value",
            "amount",
            "debt",
        ];
        return families.FirstOrDefault(name.Contains) ?? name;
    }

    public static int FieldPriority(OpenSearchFieldView field)
    {
        var name = field.Name.ToLowerInvariant();
        string[] priorities =
        [
            "profit",
            "cashflow",
            "margin",
            "yield",
            "status",
            "outcome",
            "authority",
            "council",
            "suburb",
            "locality",
            "category",
            "type",
            "source",
            "cost",
            "rent",
            "value",
            "amount",
            "debt",
            "lodged",
            "decision",
            "created",
            "updated",
            "date",
            "timestamp",
        ];
        var match = Array.FindIndex(priorities, name.Contains);
        return match < 0 ? priorities.Length : match;
    }

    public static string Humanize(string field)
    {
        var leaf = field.Split('.').Last().Replace('_', ' ').Replace('-', ' ');
        return string.Join(
            ' ',
            leaf.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..])
        );
    }

    public void BucketChanged()
    {
        var field = Fields.FirstOrDefault(item => item.Name == BucketField);
        BucketType = field?.Type == "date" ? "date_histogram" : "terms";
        if (MetricType != "count" && string.IsNullOrWhiteSpace(MetricField))
            MetricField = NumericFields.FirstOrDefault()?.Name ?? "";
    }

    public async Task SearchAsync()
    {
        Searching = true;
        Error = null;
        try
        {
            Result = await Svc.SearchOpenSearchAsync(BuildRequest());
            LastPreviewSpec = null;
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            Searching = false;
        }
    }

    public OpenSearchSearchRequest BuildRequest() =>
        new(
            ProjectId,
            IndexPattern,
            QueryText,
            Page,
            PageSize,
            NullIfBlank(BucketField),
            BucketType,
            ChartType,
            MetricType,
            NullIfBlank(MetricField),
            DateInterval
        );

    public async Task SaveDashboardAsync()
    {
        if (Result?.ChartSpecJson is null)
            return;
        if (string.IsNullOrWhiteSpace(DashboardName))
        {
            Error = "Enter a name before saving the chart.";
            return;
        }
        SavingDashboard = true;
        try
        {
            var saved = await Svc.SaveOpenSearchDashboardAsync(
                new SaveOpenSearchDashboardCommand(
                    ProjectId,
                    DashboardName,
                    IndexPattern,
                    QueryText,
                    BucketField,
                    BucketType,
                    ChartType,
                    MetricType,
                    NullIfBlank(MetricField),
                    DateInterval,
                    Result.ChartSpecJson,
                    EditingDashboardId
                )
            );
            ReplaceDashboard(saved);
            EditingDashboardId = saved.Id;
            RenderedDashboards.Remove(saved.Id);
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            SavingDashboard = false;
        }
    }

    public async Task LoadDashboardAsync(OpenSearchDashboardView dashboard)
    {
        EditingDashboardId = dashboard.Id;
        DashboardName = dashboard.Name;
        IndexPattern = dashboard.IndexPattern;
        QueryText = dashboard.QueryText ?? "";
        BucketField = dashboard.BucketField;
        BucketType = dashboard.BucketType;
        ChartType = dashboard.ChartType;
        MetricType = dashboard.MetricType;
        MetricField = dashboard.MetricField ?? "";
        DateInterval = dashboard.DateInterval ?? "day";
        Page = 1;
        await LoadFieldsAsync();
        await SearchAsync();
    }

    public async Task RefreshDashboardAsync(OpenSearchDashboardView dashboard)
    {
        Refreshing = dashboard.Id;
        try
        {
            var request = new OpenSearchSearchRequest(
                ProjectId,
                dashboard.IndexPattern,
                dashboard.QueryText,
                1,
                PageSize,
                dashboard.BucketField,
                dashboard.BucketType,
                dashboard.ChartType,
                dashboard.MetricType,
                dashboard.MetricField,
                dashboard.DateInterval
            );
            var result = await Svc.SearchOpenSearchAsync(request);
            if (result.ChartSpecJson is null)
                throw new InvalidOperationException(
                    "The refreshed query returned no chart buckets."
                );
            var saved = await Svc.SaveOpenSearchDashboardAsync(
                new SaveOpenSearchDashboardCommand(
                    ProjectId,
                    dashboard.Name,
                    dashboard.IndexPattern,
                    dashboard.QueryText,
                    dashboard.BucketField,
                    dashboard.BucketType,
                    dashboard.ChartType,
                    dashboard.MetricType,
                    dashboard.MetricField,
                    dashboard.DateInterval,
                    result.ChartSpecJson,
                    dashboard.Id
                )
            );
            ReplaceDashboard(saved);
            RenderedDashboards.Remove(saved.Id);
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            Refreshing = null;
        }
    }

    public async Task DuplicateDashboardAsync(OpenSearchDashboardView dashboard)
    {
        Replicating = dashboard.Id;
        try
        {
            var baseName = $"{dashboard.Name} copy";
            var name = baseName;
            var suffix = 2;
            while (
                Dashboards.Any(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            )
                name = $"{baseName} {suffix++}";

            var duplicate = await Svc.SaveOpenSearchDashboardAsync(
                new SaveOpenSearchDashboardCommand(
                    ProjectId,
                    name,
                    dashboard.IndexPattern,
                    dashboard.QueryText,
                    dashboard.BucketField,
                    dashboard.BucketType,
                    dashboard.ChartType,
                    dashboard.MetricType,
                    dashboard.MetricField,
                    dashboard.DateInterval,
                    dashboard.ChartSpecJson
                )
            );
            ReplaceDashboard(duplicate);
            RenderedDashboards.Remove(duplicate.Id);
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            Replicating = null;
        }
    }

    public async Task DeleteDashboardAsync(Guid id)
    {
        try
        {
            if (await Svc.DeleteOpenSearchDashboardAsync(id))
            {
                Dashboards.RemoveAll(item => item.Id == id);
                RenderedDashboards.Remove(id);
                if (EditingDashboardId == id)
                    EditingDashboardId = null;
            }
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
    }

    public async Task PreviousPageAsync()
    {
        if (Page <= 1)
            return;
        Page--;
        await SearchAsync();
    }

    public async Task NextPageAsync()
    {
        Page++;
        await SearchAsync();
    }

    public async Task OnQueryKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            Page = 1;
            await SearchAsync();
        }
    }

    public void Dispose() => Detach();

    public void ReplaceDashboard(OpenSearchDashboardView dashboard)
    {
        var index = Dashboards.FindIndex(item => item.Id == dashboard.Id);
        if (index < 0)
            Dashboards.Add(dashboard);
        else
            Dashboards[index] = dashboard;
        Dashboards = Dashboards.OrderBy(item => item.Name).ToList();
    }

    public static string CanvasId(string value) =>
        "oschart-"
        + Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value))
        )[..12];

    public string CanvasIdFor(Guid dashboardId) => CanvasId(dashboardId.ToString());

    public static string GeneratedCanvasId(string value) =>
        "osgenerated-"
        + Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(value))
        )[..12];

    public static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? Value(OpenSearchHitView hit, string field) =>
        hit.Fields.GetValueOrDefault(field);

    public static string ShortValue(string? value) =>
        value is null ? "—"
        : value.Length > 90 ? value[..87] + "…"
        : value;

    public static string FriendlyError(Exception ex) =>
        ex is UnauthorizedAccessException
            ? "You do not have permission to search project data."
            : ex.Message;

    public sealed record GeneratedOpenSearchChart(
        string Id,
        string Title,
        string Subtitle,
        string ChartSpecJson
    );

    public sealed record GeneratedChartCandidate(
        string Id,
        string Title,
        string Subtitle,
        string BucketField,
        string BucketType,
        string ChartType,
        string MetricType,
        string? MetricField,
        string? DateInterval
    );
}
