using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    // ── Helpers used by markup ────────────────────────────────────────────────────────────────
    /// <summary>UI-only form key (<c>step0:address</c>) — never sent as a job stdin key.</summary>
    public static string ArgKey(int stepIndex, string param) => ParameterPromptState.ChainArgKey(stepIndex, param);

    public string JobName(Guid jobId) => Jobs?.FirstOrDefault(j => j.Id == jobId)?.Name ?? jobId.ToString("N")[..8];

    public static string StatusColor(string status) => StatusHelper.Color(status);
    public static string StatusBg(string status) => StatusHelper.Background(status);
    public static string FormatDuration(DateTimeOffset start, DateTimeOffset end) => FormatHelper.Duration(start, end);
    public static string PrettyJson(string raw) => FormatHelper.PrettyJson(raw);

}
