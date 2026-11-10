using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Cluster;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class OnboardingViewModel(
    PlaceContextService service,
    NavigationManager navigation,
    PortalUiState ui) : PageViewModel
{
    public bool Loading { get; private set; }
    public string? Error { get; private set; }
    public SetupGuideStep Step { get; private set; }
    public ExecutionLocation? Location { get; private set; }
    public ClusterInfo? Cluster { get; private set; }
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
        if (Loading)
        {
            return;
        }
        ui.Set("Cluster setup", "choose where your jobs run");
        Loading = true;
        Error = null;
        Cluster = null;
        NotifyStateChanged();
        try
        {
            Cluster = await service.GetClusterInfoAsync();
        }
        catch
        {
            Error = "Could not load the cluster. You can still choose a setup path and retry the connection check.";
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
        Step = SetupGuideStep.Cluster;
        NotifyStateChanged();
    }

    public void Back()
    {
        Step = SetupGuideStep.Location;
        NotifyStateChanged();
    }

    public void OpenCluster() => navigation.NavigateTo(PageRoutes.ClusterSettings);
    public void SkipGuide() => navigation.NavigateTo("/");
}
