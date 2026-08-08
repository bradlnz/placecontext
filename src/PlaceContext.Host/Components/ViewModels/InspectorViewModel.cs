using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class InspectorViewModel : PageViewModel, IDisposable
{
    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private Timer? _poll;

    public InspectorViewModel(IPlaceContextService service, PortalUiState ui) =>
        (_service, _ui) = (service, ui);

    public IReadOnlyList<ToolCallView>? Calls { get; private set; }
    public ToolCallView? Active { get; private set; }

    public async Task LoadAsync()
    {
        Calls = await _service.GetRecentToolCallsAsync(InspectorPageConstants.ToolCallLimit);
        Active ??= Calls.FirstOrDefault();
        NotifyStateChanged();
    }

    public void Select(ToolCallView call)
    {
        Active = call;
        NotifyStateChanged();
    }

    public void StartPolling()
    {
        _ui.Set(InspectorPageConstants.Title, InspectorPageConstants.Subtitle);
        _poll = new Timer(
            _ => _ = LoadAsync(),
            null,
            InspectorPageConstants.PollInterval,
            InspectorPageConstants.PollInterval
        );
    }

    public static string StatusColor(string status) =>
        status switch
        {
            InspectorStatuses.Ok => "var(--good)",
            InspectorStatuses.Warn => "var(--warn)",
            _ => "var(--bad)",
        };

    public static string StatusBackground(string status) =>
        status switch
        {
            InspectorStatuses.Ok => "var(--good-bg)",
            InspectorStatuses.Warn => "var(--warn-bg)",
            _ => "var(--bad-bg)",
        };

    public void Dispose() => _poll?.Dispose();
}
