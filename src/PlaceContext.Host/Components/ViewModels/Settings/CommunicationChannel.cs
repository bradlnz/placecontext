using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Host;
using PlaceContext.Infrastructure.Comms;

namespace PlaceContext.Host.Components.ViewModels;

public enum CommunicationChannel
{
    Email,
    Sms,
}
