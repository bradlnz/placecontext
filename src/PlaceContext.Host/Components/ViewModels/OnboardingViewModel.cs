using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.OpenSearch;
using PlaceContext.Infrastructure.ProjectData;

namespace PlaceContext.Host.Components.ViewModels;

public enum SetupGuideStep
{
    DataSource,
    OpenSearch,
    Ready,
}

public sealed record SetupDataSourceOption(
    string Key,
    string Title,
    string Description,
    string ActionLabel);

/// <summary>
/// First-login workspace guide. It discovers configured sources before asking the operator to add
/// another one, then treats OpenSearch as a separate optional deployment decision.
/// </summary>
public sealed class OnboardingViewModel(
    PlaceContextService service,
    IOpenSearchConnectionResolver openSearchConnections,
    IConfiguration configuration,
    NavigationManager navigation,
    PortalUiState ui) : PageViewModel
{
    public const string ExistingOpenSearch = "existing";
    public const string InstallOpenSearch = "install";
    public const string SkipOpenSearch = "skip";

    public static readonly IReadOnlyList<SetupDataSourceOption> DataSourceOptions =
    [
        new("workspace-database", "Workspace database",
            "Create a table or import a CSV into the PostgreSQL database included with PlaceContext.",
            "Open SQL Studio"),
        new("postgresql", "External PostgreSQL",
            "Use an existing PostgreSQL database for this project's tables, records, and SQL jobs.",
            "Connect PostgreSQL"),
        new("mcp", "MCP server or API",
            "Connect HTTP, SSE, or local stdio tools, with bearer, API-key, header, or OAuth authentication.",
            "Add MCP server"),
        new("webhook", "Webhook or API push",
            "Accept JSON events at an authenticated endpoint and pass each request to event-triggered jobs.",
            "Set up endpoint"),
        new("opensearch", "OpenSearch or Elasticsearch",
            "Use an existing search endpoint as the project's searchable document source.",
            "Connect search"),
        new("job", "Job or pipeline output",
            "Bring in any other source with a job, then map structured results into project data.",
            "Create data job"),
    ];

    public IReadOnlyList<ProjectSummaryView> Projects { get; private set; } =
        Array.Empty<ProjectSummaryView>();
    public Guid? ProjectId { get; set; }
    public bool Loading { get; private set; } = true;
    public string? Error { get; private set; }
    public SetupGuideStep Step { get; private set; } = SetupGuideStep.DataSource;
    public string? SelectedDataSource { get; private set; }
    public string? OpenSearchChoice { get; private set; }
    public bool HasConfiguredDataSource { get; private set; }
    public bool OpenSearchConfigured { get; private set; }
    public IReadOnlyList<string> DetectedSources { get; private set; } = Array.Empty<string>();

    public string? ProjectName => Projects.FirstOrDefault(project => project.Id == ProjectId)?.Name;
    public SetupDataSourceOption? SelectedOption =>
        DataSourceOptions.FirstOrDefault(option => option.Key == SelectedDataSource);
    public bool CanContinue => SelectedDataSource is not null;
    public bool CanFinish => OpenSearchChoice is not null;
    public string OpenSearchSummary => OpenSearchChoice switch
    {
        ExistingOpenSearch when OpenSearchConfigured => "Configured",
        ExistingOpenSearch => "Connect existing",
        InstallOpenSearch => "Installation needed",
        _ => "Not now",
    };
    public string NextLabel => OpenSearchChoice switch
    {
        InstallOpenSearch => "Open installation guide",
        ExistingOpenSearch when !OpenSearchConfigured => "Configure OpenSearch",
        _ when SelectedOption is { } option => option.ActionLabel,
        _ => "Go to dashboard",
    };

    public async Task LoadAsync()
    {
        ui.Set("Setup guide", "connect your first data source");
        Loading = true;
        Error = null;
        NotifyStateChanged();

        try
        {
            Projects = await service.GetProjectsAsync();
            ProjectId ??= Projects.FirstOrDefault()?.Id;
            await DetectSourcesAsync();
            if (HasConfiguredDataSource)
                Step = SetupGuideStep.OpenSearch;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public async Task ProjectChangedAsync(ChangeEventArgs args)
    {
        ProjectId = Guid.TryParse(args.Value?.ToString(), out var id) ? id : null;
        await DetectSourcesAsync();
        NotifyStateChanged();
    }

    public void SelectDataSource(string key)
    {
        if (DataSourceOptions.Any(option => option.Key == key))
            SelectedDataSource = key;
        NotifyStateChanged();
    }

    public void ContinueToOpenSearch()
    {
        if (!CanContinue && !HasConfiguredDataSource)
            return;
        Step = SetupGuideStep.OpenSearch;
        NotifyStateChanged();
    }

    public void SelectOpenSearch(string choice)
    {
        if (choice is ExistingOpenSearch or InstallOpenSearch or SkipOpenSearch)
            OpenSearchChoice = choice;
        NotifyStateChanged();
    }

    public void Back()
    {
        Step = Step switch
        {
            SetupGuideStep.Ready => SetupGuideStep.OpenSearch,
            SetupGuideStep.OpenSearch when !HasConfiguredDataSource => SetupGuideStep.DataSource,
            _ => Step,
        };
        NotifyStateChanged();
    }

    public void Finish()
    {
        if (!CanFinish)
            return;
        Step = SetupGuideStep.Ready;
        NotifyStateChanged();
    }

    public void OpenNextStep()
    {
        if (OpenSearchChoice == InstallOpenSearch)
        {
            navigation.NavigateTo(PageRoutes.WikiArticle("opensearch-integration"));
            return;
        }

        if (OpenSearchChoice == ExistingOpenSearch && !OpenSearchConfigured)
        {
            navigation.NavigateTo(PageRoutes.ConnectionsSettings);
            return;
        }

        navigation.NavigateTo(DataSourceRoute(SelectedDataSource, ProjectId));
    }

    public void SkipGuide() => navigation.NavigateTo("/");

    public static string DataSourceRoute(string? source, Guid? projectId) => source switch
    {
        "postgresql" or "opensearch" => PageRoutes.ConnectionsSettings,
        "mcp" => PageRoutes.McpSettings,
        "webhook" => PageRoutes.WebhookIngestionWiki,
        "job" when projectId is { } id => PageRoutes.ProjectJobs(id),
        _ when projectId is { } id => PageRoutes.ProjectData(id),
        _ => "/",
    };

    public static string SourceMark(string key) => key switch
    {
        "workspace-database" => "DB",
        "postgresql" => "PG",
        "mcp" => "MCP",
        "webhook" => "API",
        "opensearch" => "OS",
        _ => "{}",
    };

    private async Task DetectSourcesAsync()
    {
        var detected = new List<string>();
        OpenSearchConfigured = false;
        if (ProjectId is not { } projectId)
        {
            DetectedSources = detected;
            HasConfiguredDataSource = false;
            return;
        }

        var tablesTask = service.ListProjectDataTablesAsync(projectId);
        var secretsTask = service.ListProjectSecretsAsync(projectId);
        var mcpTask = service.ListMcpConnectionsAsync(projectId);
        await Task.WhenAll(tablesTask, secretsTask, mcpTask);

        var tables = await tablesTask;
        var secrets = await secretsTask;
        var mcpConnections = await mcpTask;

        if (tables.Count > 0)
            detected.Add($"Workspace database ({tables.Count} table{(tables.Count == 1 ? "" : "s")})");
        if (secrets.Any(secret => secret.Name == ProjectDatabaseConnectionResolver.HostVariable))
            detected.Add("External PostgreSQL");
        if (mcpConnections.Any(connection => connection.Enabled))
            detected.Add("MCP server");
        if (!string.IsNullOrWhiteSpace(configuration["PlaceContext:Ingest:Key"]))
            detected.Add("Webhook / ingestion API");

        OpenSearchConfigured = await openSearchConnections.ResolveAsync(projectId) is not null;
        if (OpenSearchConfigured)
            detected.Add("OpenSearch");

        DetectedSources = detected;
        HasConfiguredDataSource = detected.Count > 0;
    }
}
