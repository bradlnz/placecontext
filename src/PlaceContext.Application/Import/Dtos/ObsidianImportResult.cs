using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>What one vault import produced.</summary>
public sealed record ObsidianImportResult(int Notes, int Links);
