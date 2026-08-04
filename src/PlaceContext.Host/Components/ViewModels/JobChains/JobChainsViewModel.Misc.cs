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
    // ── Chains state ──────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<JobChainView>? Chains { get; private set; }
    public IReadOnlyList<JobView>? Jobs { get; private set; }
    public string? Message { get; private set; }
    public bool Loading { get; private set; } = true;
    public Guid? ConfirmDeleteId { get; set; }
}
