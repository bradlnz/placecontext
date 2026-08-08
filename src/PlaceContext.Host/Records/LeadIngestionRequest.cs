using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Crm.Infrastructure.Crm;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

public sealed record LeadIngestionRequest(
    string? Name,
    string? Email,
    string? Phone,
    string? Company,
    string? Message,
    string? Source,
    string? Address,
    Dictionary<string, JsonElement>? Metadata,
    string? Website);
