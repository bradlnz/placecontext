using System.Text.Json.Nodes;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Creates or refreshes a user-defined SQL chart: the query runs INSIDE the project's database —
/// as the project's own Postgres role with search_path pinned to its schema — so it can only ever
/// see that project's data. The result is folded into a chart spec (first text column = labels,
/// numeric columns = series) and stored under the reserved <c>sql:{name}</c> chart slot with the
/// query embedded, so it can be re-run and edited later.
/// </summary>
public sealed record SaveSqlChartCommand(Guid ProjectId, string Name, string Sql, string ChartType = "bar") : ICommand<ProjectChartView>;
