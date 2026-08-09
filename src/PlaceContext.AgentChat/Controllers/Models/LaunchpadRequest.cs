using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;

namespace PlaceContext.AgentChat.Controllers;

public sealed record LaunchpadRequest(
    Guid ProjectId,
    string TriggerName,
    string Prompt,
    string? SourceTable,
    Guid ChainId);
