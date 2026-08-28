using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class InspectorViewModel : PageViewModel, IDisposable
{
    private readonly PlaceContextService _service;
    private readonly PortalUiState _ui;
    private Timer? _poll;

    public InspectorViewModel(PlaceContextService service, PortalUiState ui) =>
        (_service, _ui) = (service, ui);

    public IReadOnlyList<ToolCallView>? Calls { get; private set; }
    public ToolCallView? Active { get; private set; }

    public async Task LoadAsync()
    {
        Calls = await _service.GetRecentToolCallsAsync(PageConstants.ToolCallLimit);
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
        _ui.Set(PageConstants.Title, PageConstants.Subtitle);
        _poll = new Timer(
            _ => _ = LoadAsync(),
            null,
            PageConstants.PollInterval,
            PageConstants.PollInterval
        );
    }

    public static string StatusColor(string status) =>
        status switch
        {
            Statuses.Ok => "var(--good)",
            Statuses.Warn => "var(--warn)",
            _ => "var(--bad)",
        };

    public static string StatusBackground(string status) =>
        status switch
        {
            Statuses.Ok => "var(--good-bg)",
            Statuses.Warn => "var(--warn-bg)",
            _ => "var(--bad-bg)",
        };

    private static class Statuses
    {
        public const string Ok = "Ok";
        public const string Warn = "Warn";
    }

    private static class PageConstants
    {
        public const string Title = "MCP Inspector";
        public const string Subtitle = "live tool traffic · MCP via Streamable HTTP";
        public const int ToolCallLimit = 20;
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    }

    public void Dispose() => _poll?.Dispose();
}
