using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>
/// Builds the relation tree automatically: after a run completes, its primary JSON artifact's
/// string values are matched against each entity's key values (the label + relation columns of the
/// tagged table, read as the project's isolated role). A match — say, an address that exists on the
/// sites entity — persists a tag linking run ⇄ entity record, which transitively links the job and
/// the run's artifacts to that record. Entirely best-effort and capped; tagging never fails a run.
/// </summary>
public sealed class EntityTagService
{
    private const int MaxArtifactValues = 400;
    private const int MaxKeysPerEntity = 1000;

    private readonly IDataEntityRepository _entities;
    private readonly IProjectDataStore _store;
    private readonly IEntityTagStore _tags;
    private readonly IDocumentTextExtractor? _docText;
    private readonly ILogger<EntityTagService>? _log;

    public EntityTagService(IDataEntityRepository entities, IProjectDataStore store, IEntityTagStore tags,
        IDocumentTextExtractor? docText = null, ILogger<EntityTagService>? log = null)
    {
        _entities = entities;
        _store = store;
        _tags = tags;
        _docText = docText;
        _log = log;
    }

    public async Task TagRunAsync(Job job, JobRun run, CancellationToken ct = default)
    {
        try
        {
            var artifactValues = CollectStrings(run);
            var corpus = BuildDocumentCorpus(run, _docText);
            if (artifactValues.Count == 0 && corpus.Length == 0) return;

            var entities = await _entities.ListForProjectAsync(run.ProjectId, ct);
            if (entities.Count == 0) return;

            var found = new List<EntityTag>();
            foreach (var entity in entities)
            {
                // Literal tags declared on the entity map the same way its column values do: an
                // exact hit in the JSON output, or a substring inside an emitted document.
                foreach (var tag in entity.Tags)
                {
                    if (tag.Length < 2) continue;
                    if (artifactValues.Contains(tag)
                        || (tag.Length > 3 && corpus.Contains(tag, StringComparison.OrdinalIgnoreCase)))
                        found.Add(new EntityTag(run.ProjectId, entity.Id, entity.Name, tag, run.Id, job.Id));
                }

                foreach (var column in KeyColumns(entity))
                {
                    IReadOnlyList<IReadOnlyList<string?>> rows;
                    try
                    {
                        var r = await _store.ExecuteAsync(run.ProjectId,
                            $"SELECT DISTINCT \"{column.Replace("\"", "")}\"::text FROM \"{entity.TableName.Replace("\"", "")}\" LIMIT {MaxKeysPerEntity}", ct);
                        rows = r.Rows;
                    }
                    catch { continue; } // column/table missing — the tag pass skips, never fails

                    foreach (var row in rows)
                    {
                        if (row.Count == 0 || row[0] is not { Length: > 2 } key) continue;
                        // Exact match against the JSON output's values, or a substring hit inside
                        // emitted documents (a PDF/HTML report that mentions a known address).
                        if (artifactValues.Contains(key)
                            || (key.Length > 3 && corpus.Contains(key, StringComparison.Ordinal)))
                            found.Add(new EntityTag(run.ProjectId, entity.Id, entity.Name, key, run.Id, job.Id));
                    }
                }
            }

            if (found.Count > 0)
            {
                await _tags.AddAsync(found.DistinctBy(t => (t.EntityId, t.Key)).ToList(), ct);
                _log?.LogInformation("Tagged run {RunId} with {Count} entity key(s).", run.Id, found.Count);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Entity tagging failed for run {RunId} — the run itself is unaffected.", run.Id);
        }
    }

    // The values that identify a record: the label column plus every relation key column.
    private static IEnumerable<string> KeyColumns(DataEntity entity)
    {
        if (entity.LabelColumn is { Length: > 0 } label) yield return label;
        foreach (var rel in entity.Relations)
            yield return rel.Column;
    }

    private const int MaxCorpusChars = 1_000_000;

    // Text pulled out of the run's DOCUMENT artifacts — HTML with tags stripped, PDFs via their
    // content streams — so documents participate in tagging, not just the JSON output.
    private static string BuildDocumentCorpus(JobRun run, IDocumentTextExtractor? docText)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var artifact in run.ShardResults.SelectMany(s => s.Artifacts)
                     .Concat(run.ReduceResult?.Artifacts ?? (IReadOnlyList<RunArtifact>)Array.Empty<RunArtifact>()))
        {
            if (sb.Length >= MaxCorpusChars) break;
            var name = artifact.Name.ToLowerInvariant();
            try
            {
                if (!artifact.IsBinary && (name.EndsWith(".html") || name.EndsWith(".htm")
                    || name.EndsWith(".txt") || name.EndsWith(".md") || name.EndsWith(".csv")))
                {
                    sb.Append('\n').Append(StripTags(artifact.Content));
                }
                else if (name.EndsWith(".pdf"))
                {
                    // The real extractor decodes compressed/subsetted PDFs (reportlab et al.);
                    // the byte-level sniff below only covers uncompressed literals.
                    var bytes = artifact.GetBytes();
                    sb.Append('\n').Append(docText?.ExtractText(bytes, artifact.Name) ?? PdfText(bytes));
                }
            }
            catch { /* one unreadable document never blocks the tag pass */ }
        }
        return sb.Length > MaxCorpusChars ? sb.ToString(0, MaxCorpusChars) : sb.ToString();
    }

    private static string StripTags(string html)
        => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");

    /// <summary>
    /// Zero-dependency PDF text sniff: inflate each FlateDecode content stream (PDF streams are
    /// zlib) and pull out the literal show-strings — enough to find entity keys in reports from
    /// reportlab and similar generators. Best-effort by design; exotic encodings simply don't match.
    /// </summary>
    internal static string PdfText(byte[] pdf)
    {
        var sb = new System.Text.StringBuilder();
        var span = pdf.AsSpan();
        var streamMarker = "stream"u8;
        var endMarker = "endstream"u8;
        var pos = 0;
        while (pos < span.Length && sb.Length < MaxCorpusChars)
        {
            var start = span[pos..].IndexOf(streamMarker);
            if (start < 0) break;
            start = pos + start + streamMarker.Length;
            while (start < span.Length && (span[start] == (byte)'\r' || span[start] == (byte)'\n')) start++;
            var end = span[start..].IndexOf(endMarker);
            if (end < 0) break;
            var body = pdf.AsSpan(start, end).ToArray();
            pos = start + end + endMarker.Length;

            byte[] text = body;
            if (body.Length > 2 && body[0] == 0x78) // zlib header — FlateDecode
            {
                try
                {
                    using var input = new MemoryStream(body, 2, body.Length - 2);
                    using var deflate = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    deflate.CopyTo(output, 81920);
                    text = output.ToArray();
                }
                catch { continue; }
            }

            // PDF literal show-strings live in parentheses: (233 Gympie Rd) Tj
            var decoded = System.Text.Encoding.Latin1.GetString(text);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(decoded, @"\(((?:[^()\\]|\\.)*)\)"))
                sb.Append(m.Groups[1].Value.Replace("\\(", "(").Replace("\\)", ")")).Append(' ');
        }
        return sb.ToString();
    }

    // Every string value in the run's primary artifact JSON (capped) — what the run "mentioned".
    private static HashSet<string> CollectStrings(JobRun run)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        var primary = run.ReduceResult?.Artifact
            ?? run.ShardResults.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Artifact))?.Artifact;
        if (string.IsNullOrWhiteSpace(primary)) return values;
        try
        {
            using var doc = JsonDocument.Parse(primary);
            Walk(doc.RootElement, values);
        }
        catch { /* not JSON — nothing to tag against */ }
        return values;

        static void Walk(JsonElement el, HashSet<string> into)
        {
            if (into.Count >= MaxArtifactValues) return;
            switch (el.ValueKind)
            {
                case JsonValueKind.String when el.GetString() is { Length: > 2 } s:
                    into.Add(s);
                    break;
                case JsonValueKind.Object:
                    foreach (var p in el.EnumerateObject()) Walk(p.Value, into);
                    break;
                case JsonValueKind.Array:
                    foreach (var i in el.EnumerateArray()) Walk(i, into);
                    break;
            }
        }
    }
}
