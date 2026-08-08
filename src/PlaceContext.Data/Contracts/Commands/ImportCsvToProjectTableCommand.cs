using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>
/// Import parsed CSV rows into a project table. When <paramref name="CreateTable"/> is true the table
/// is created from <paramref name="Columns"/> first; otherwise the rows are appended to an existing
/// project-owned table with a matching column order.
/// </summary>
public sealed record ImportCsvToProjectTableCommand(
    Guid ProjectId,
    string TableName,
    IReadOnlyList<ProjectColumnSpec> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    bool CreateTable) : ICommand<ImportCsvResult>;
