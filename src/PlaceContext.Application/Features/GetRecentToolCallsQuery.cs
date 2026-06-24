using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

// ---- Recent MCP tool calls (Inspector) ----

public sealed record GetRecentToolCallsQuery(int Take = 100) : IQuery<IReadOnlyList<ToolCallView>>;
