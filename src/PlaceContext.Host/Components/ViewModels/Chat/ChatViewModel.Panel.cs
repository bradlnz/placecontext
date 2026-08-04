using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Mcp;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Infrastructure.Caching;
using PlaceContext.Infrastructure.Chat;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class ChatViewModel
{
    // ── Panel artifacts ──────────────────────────────────────────────────────

    private async Task LoadPanelArtifactsAsync()
    {
        if (!ProjectId.HasValue)
            return;
        try
        {
            var artifacts = await _svc.ListProjectArtifactsAsync(ProjectId.Value, 8, null);
            PanelArtifacts.Clear();
            PanelArtifacts.AddRange(artifacts);
        }
        catch { }
    }

    public void MergePanelArtifacts(IEnumerable<ArtifactFileView> found)
    {
        var incoming = found.ToList();
        if (incoming.Count == 0)
            return;
        PanelArtifacts.RemoveAll(a => incoming.Any(f => f.Id == a.Id));
        PanelArtifacts.InsertRange(0, incoming);
        if (PanelArtifacts.Count > 10)
            PanelArtifacts.RemoveRange(10, PanelArtifacts.Count - 10);
    }

    // ── Graph ────────────────────────────────────────────────────────────────

    private async Task LoadGraphAsync()
    {
        if (!ProjectId.HasValue)
            return;
        try
        {
            var graph = await _svc.GetGraphVizAsync(ProjectId.Value);
            GraphNodes.Clear();
            GraphNodes.AddRange(graph.Nodes);
            GraphLinks = graph.LinkCount;
        }
        catch { }
    }
}
