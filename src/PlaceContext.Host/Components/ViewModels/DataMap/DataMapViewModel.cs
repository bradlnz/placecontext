using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using Microsoft.JSInterop;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class DataMapViewModel : PageViewModel
{
    private readonly IPlaceContextService _svc;
    private readonly IJSRuntime _js;

    public DataMapViewModel(IPlaceContextService svc, IJSRuntime js)
    {
        _svc = svc;
        _js = js;
    }

    // ── Parameters ────────────────────────────────────────────────────────────────────────────
    public Guid ProjectId { get; private set; }

    // ── Data state ────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<DataMappingView>? Mappings { get; private set; }
    public IReadOnlyList<JobView>? Jobs { get; private set; }
    public IReadOnlyList<ProjectTableInfo>? Tables { get; private set; }
    public string? Message { get; private set; }
    public bool Loading { get; private set; } = true;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────
    public void Initialize(Guid projectId) => ProjectId = projectId;

    public async Task LoadAsync()
    {
        Loading = true;
        Message = null;
        try
        {
            Mappings = (await _svc.ListDataMappingsAsync(ProjectId))
                .Where(mapping => !string.Equals(mapping.SourceKind, "chain", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Jobs = await _svc.ListJobsAsync(ProjectId);
            try { Tables = await _svc.ListProjectDataTablesAsync(ProjectId); }
            catch { Tables = Array.Empty<ProjectTableInfo>(); }
        }
        catch (Exception ex) { Message = ex.Message; }
        finally { Loading = false; NotifyStateChanged(); }
    }

    public async Task EnsureLayoutAsync()
    {
        if (PosLoaded) return;
        PosLoaded = true;
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", $"pc-datamap-{ProjectId}");
            if (!string.IsNullOrEmpty(json))
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double[]>>(json);
                if (saved is not null)
                    foreach (var (k, v) in saved.Where(kv => kv.Value.Length == 2))
                        Pos[k] = (v[0], v[1]);
            }
        }
        catch { }
        DefaultLayout();
    }

    private void DefaultLayout()
    {
        var yJ = 24d;
        foreach (var j in Jobs ?? Array.Empty<JobView>())
        {
            Pos.TryAdd("job:" + j.Id, (30, yJ));
            yJ += 84;
        }
        var yT = 24d;
        foreach (var t in TableNodes())
        {
            Pos.TryAdd("table:" + t, (660, yT));
            yT += 96;
        }
    }

    public async Task SavePositionsAsync()
    {
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(
                Pos.ToDictionary(kv => kv.Key, kv => new[] { kv.Value.X, kv.Value.Y }));
            await _js.InvokeVoidAsync("localStorage.setItem", $"pc-datamap-{ProjectId}", payload);
        }
        catch { }
    }

}
