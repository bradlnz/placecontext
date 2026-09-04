using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public static class ParameterInputCatalog
{
    public static ParameterInputType Parse(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "select" => ParameterInputType.Select,
            "number" => ParameterInputType.Number,
            "date" => ParameterInputType.Date,
            "datetime" or "datetime-local" => ParameterInputType.DateTime,
            "time" => ParameterInputType.Time,
            "checkbox" => ParameterInputType.Checkbox,
            "file" => ParameterInputType.File,
            _ => ParameterInputType.Text,
        };
}
