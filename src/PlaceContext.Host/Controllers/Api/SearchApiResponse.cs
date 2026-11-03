using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

public sealed record SearchApiResponse(
    string Query,
    Guid ProjectId,
    int Count,
    IReadOnlyList<SearchApiHitResponse> Hits);
