using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

public sealed class ReplayChainRequest
{
    /// <summary>The original chain run to replay from.</summary>
    public Guid OriginalRunId { get; set; }

    /// <summary>0-based step index to resume from. Null = first failed step.</summary>
    public int? FromStepIndex { get; set; }

    /// <summary>Optional input payload override for the replay start step.</summary>
    public string? InputPayload { get; set; }

    /// <summary>Optional per-step parameter overrides (keyed by step index).</summary>
    public IReadOnlyDictionary<int, string>? StepPayloadOverrides { get; set; }
}
