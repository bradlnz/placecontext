using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

public sealed record EntityApiResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string TableName,
    string? LabelColumn,
    string Slug,
    IReadOnlyList<EntityRelationResponse> Relations,
    IReadOnlyList<string> Tags,
    DateTimeOffset UpdatedAt);
