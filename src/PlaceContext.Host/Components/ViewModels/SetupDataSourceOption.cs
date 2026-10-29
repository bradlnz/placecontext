using Microsoft.AspNetCore.Components;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.OpenSearch;
using PlaceContext.Infrastructure.ProjectData;

namespace PlaceContext.Host.Components.ViewModels;

public sealed record SetupDataSourceOption(
    string Key,
    string Title,
    string Description,
    string ActionLabel);
