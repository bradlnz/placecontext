using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Mcp.Controllers;

public sealed record JobEnvironmentRequest(IReadOnlyList<Guid> ConnectionIds);
