using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Dtos;
using PlaceContext.Application;
using PlaceContext.Host;

namespace PlaceContext.Host.Components.ViewModels;

public sealed record CommunicationKindOption(
    CommunicationProviderKind Kind,
    CommunicationChannel Channel
);
