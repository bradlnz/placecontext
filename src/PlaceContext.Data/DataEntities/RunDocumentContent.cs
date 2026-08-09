using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record RunDocumentContent(string Name, string Content, bool IsBinary);

/// <summary>
/// Builds the relation tree automatically: after a run completes, its primary JSON artifact's
/// string values are matched against each entity's key values (the label + relation columns of the
/// tagged table, read as the project's isolated role). A match — say, an address that exists on the
/// sites entity — persists a tag linking run ⇄ entity record, which transitively links the job and
/// the run's artifacts to that record. Entirely best-effort and capped; tagging never fails a run.
/// </summary>
