using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

/// <summary>Which resource list the SQL Studio sidebar is showing.</summary>
public enum SidebarPane
{
    Tables,
    Indexes,
    Queries,
}
