using System.Text.Json;
using System.Text.RegularExpressions;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed record JobLogEvent(int Index, string Stream, string? Timestamp, string Level, string Message);
