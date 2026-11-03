using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application;
using PlaceContext.Host.Auth;

namespace PlaceContext.Host.Controllers.Api;

public sealed record EntityRecordsResponse(
    Guid EntityId,
    string EntityName,
    string TableName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    long Total,
    int Page,
    int PageSize);
