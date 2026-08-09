using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;

namespace PlaceContext.Jobs.Controllers;

public sealed record EventRequest(string EventType, Guid ProjectId, string Payload);
