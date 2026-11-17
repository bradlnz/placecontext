using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Build;
using PlaceContext.Application.Cluster;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class OnboardingViewModel(
    PlaceContextService service,
    NavigationManager navigation,
    PortalUiState ui,
    IModelConnections modelConnections,
    Microsoft.Extensions.Options.IOptions<PlaceContext.Application.Billing.RunBillingDeploymentOptions>? hosting = null) : PageViewModel
{
    public bool Loading { get; private set; }
    public string? Error { get; private set; }
    public SetupGuideStep Step { get; private set; }
    public ExecutionLocation? Location { get; private set; }
    public ClusterInfo? Cluster { get; private set; }
    public bool HasModelSetup { get; private set; }
    public string? ModelError { get; private set; }
    public string WorkspaceName => ui.CurrentProjectName ?? "Your workspace";
    public string DashboardUrl => ui.CurrentProjectId is { } id ? $"/project/{id}/dashboard" : "/";
    public string WorkflowsUrl => ui.CurrentProjectId is { } id ? PageRoutes.ProjectWorkflows(id) : DashboardUrl;
    public string DataUrl => ui.CurrentProjectId is { } id ? PageRoutes.ProjectData(id) : DashboardUrl;
    public bool HasReadyWorker => Cluster is { IsRealCluster: true } && Cluster.Nodes.Any(node => node.Ready && !node.IsControlPlane);
    public bool CanContinue => Location is not null;
    public bool IsCloud => Location == ExecutionLocation.Cloud;
    public bool IsLocal => Location == ExecutionLocation.Local;
    public string LocationLabel => IsCloud ? "Cloud" : "Local";
    public string ClusterSummary => Cluster switch
    {
        null => "Cluster information is unavailable.",
        { IsRealCluster: false } => "This portal reports only its local host. Check the execution cluster configuration before running jobs.",
        { NodeCount: 0 } => "The cluster has no visible nodes. Check access and add a worker.",
        _ => $"{Cluster.Nodes.Count(node => node.Ready)} of {Cluster.NodeCount} nodes ready. Verify job placement with a test run.",
    };
    public string SetupUrl => IsLocal
        ? PageRoutes.WikiArticle("setup") + "#install-locally"
        : PageRoutes.WikiArticle("cluster-and-nodes") + "#cloud-workers";

    public async Task LoadAsync()
    {
        if (hosting?.Value.ManagedHosting == true)
        {
            navigation.NavigateTo("/cloud", replace: true);
            return;
        }
        if (Loading)
        {
            return;
        }
        ui.Set("Workspace setup", "get ready for your first workflow");
        Loading = true;
        Error = null;
        Cluster = null;
        HasModelSetup = false;
        ModelError = null;
        NotifyStateChanged();
        try
        {
            Cluster = await service.GetClusterInfoAsync();
        }
        catch
        {
            Error = "Could not load the cluster. You can still choose a setup path and retry the connection check.";
        }
        try
        {
            var models = await modelConnections.ListAsync();
            HasModelSetup = models.Any(connection => connection.Enabled && connection.Models.Count > 0);
        }
        catch
        {
            ModelError = "Could not check model connections. Retry the check or open model settings.";
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public void SelectLocation(ExecutionLocation location)
    {
        if (!Enum.IsDefined(location))
        {
            return;
        }
        Location = location;
        NotifyStateChanged();
    }

    public void Continue()
    {
        if (!CanContinue)
        {
            return;
        }
        if (Step < SetupGuideStep.Ready) Step++;
        NotifyStateChanged();
    }

    public void Back()
    {
        if (Step > SetupGuideStep.Location) Step--;
        NotifyStateChanged();
    }

    public void OpenCluster() => navigation.NavigateTo(PageRoutes.ClusterSettings);
    public void SkipGuide() => navigation.NavigateTo(DashboardUrl);
}
