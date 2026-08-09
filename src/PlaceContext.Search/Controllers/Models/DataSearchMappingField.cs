using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Search.Controllers;

public sealed record DataSearchMappingField(string Name, string OpenSearchType);
