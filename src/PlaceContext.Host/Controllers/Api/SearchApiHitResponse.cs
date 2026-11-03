using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

public sealed record SearchApiHitResponse(
    string Kind,
    Guid ProjectId,
    string Title,
    string Subtitle,
    string Url);
