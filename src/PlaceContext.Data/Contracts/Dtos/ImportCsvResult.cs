using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>
/// The CSV import outcome: how many rows were imported, plus warn-only notices for rows whose
/// identity values already existed in the table (the rows are kept regardless).
/// </summary>
public sealed record ImportCsvResult(int Imported, IReadOnlyList<string> DuplicateWarnings);
