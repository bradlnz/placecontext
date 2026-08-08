using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Controllers;

public sealed record OpenSearchProxySearchRequest(
    string IndexPattern,
    string? QueryText,
    int Page = 1,
    int PageSize = 25,
    string? BucketField = null,
    string BucketType = "terms",
    string ChartType = "bar",
    string MetricType = "count",
    string? MetricField = null,
    string? DateInterval = null);
