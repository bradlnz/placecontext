using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Text;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host;
using PlaceContext.Crm.Infrastructure.Crm;
using System.Net.Http.Json;
using System.Text.Json;

namespace PlaceContext.Host.Components.ViewModels.Crm;

public sealed record PortalImpersonateResponse(string Url);
