using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public enum ParameterInputType
{
    Text,
    Select,
    Number,
    Checkbox,
    File,
}
