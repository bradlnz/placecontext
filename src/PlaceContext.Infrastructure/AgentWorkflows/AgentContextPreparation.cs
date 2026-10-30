using System.Text.Json;
using PlaceContext.Application.AgentWorkflows;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.AgentWorkflows;

internal static class AgentContextPreparation
{
    internal const int MaximumInputCharacters = 16000;
    private const int MaximumPreparedCharacters = 12000;
    private const int MaximumFieldCharacters = 4500;
    private const int SampleRows = 8;
    private const int SampleColumns = 24;
    private const int CellCharacters = 256;

    internal static async Task<AgentPreparation> PrepareAsync(IProjectDataStore store, Guid projectId,
        AgentWorkflow workflow, CancellationToken ct)
    {
        // Include blank optional fields in the size check: the UI serializes the complete editor.
        var inputs = workflow.InputFields.ToDictionary(x => x, _ => "", StringComparer.Ordinal);
        var sources = new List<AgentContextSource>();
        IReadOnlyList<ProjectTableInfo> tables;
        try { tables = await store.ListTablesAsync(projectId, ct); }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return Result("Project data is currently unavailable. Try again, or add the context you want this agent to review.");
        }

        var ambiguous = false;
        var unreadable = false;
        var omitted = false;
        // Core data gets the available context budget before optional enrichment.
        foreach (var field in workflow.InputFields.OrderByDescending(x => workflow.RequiredInputFields.Contains(x)))
        {
            ct.ThrowIfCancellationRequested();
            if (!workflow.SourceAliases.TryGetValue(field, out var aliases)) continue;
            var names = aliases.Select(Normalize).ToHashSet(StringComparer.Ordinal);
            var matches = tables.Where(x => names.Contains(Normalize(x.Name))).ToArray();
            if (matches.Length > 1) { ambiguous = true; continue; }
            if (matches.Length == 0) continue;
            var table = matches[0];
            ProjectTablePageResult page;
            try
            {
                page = await store.QueryTablePageAsync(projectId, table.Name, null, 1, SampleRows,
                    ct: ct, includeTotalCount: false);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { unreadable = true; continue; }
            if (page.Columns.Count == 0 || page.Rows.Count == 0
                || !page.Rows.Any(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))) continue;

            var columns = page.Columns.Take(SampleColumns).Select(x => Clip(x, 128)).ToArray();
            var rows = page.Rows.Take(SampleRows).Select(row => row.Take(columns.Length)
                .Select(value => value is null ? null : Clip(value, CellCharacters)).ToArray()).ToArray();
            if (!rows.Any(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))) continue;
            var clipped = page.Columns.Count > SampleColumns || page.Columns.Any(x => x.Length > 128)
                || page.Rows.Take(SampleRows).Any(row => row.Take(SampleColumns).Any(x => x?.Length > CellCharacters));
            var included = 0;
            for (var count = rows.Length; count > 0; count--)
            {
                if (!rows.Take(count).Any(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))) continue;
                // Never truncate the serialized JSON itself: each candidate remains parseable evidence.
                var sample = JsonSerializer.Serialize(new
                {
                    source = table.Name,
                    sourceKind = table.IsView ? "Project view" : "Project table",
                    sourceUpdatedAt = (DateTimeOffset?)null,
                    matchBasis = "Table name matches a catalogue hint. Confirm relevance before use.",
                    sampleScope = "First rows in the store's default order; not a representative sample. Whole-table totals and freshness are unknown.",
                    sampled = true,
                    valuesOrColumnsClipped = clipped,
                    columns,
                    rows = rows.Take(count).ToArray()
                });
                if (sample.Length > MaximumFieldCharacters) continue;
                inputs[field] = sample;
                if (JsonSerializer.Serialize(inputs).Length <= MaximumPreparedCharacters) { included = count; break; }
                inputs[field] = "";
            }
            if (included == 0) { inputs[field] = ""; omitted = true; continue; }
            sources.Add(new(table.Name, table.IsView ? "Project view" : "Project table", null,
                $"{included} sampled row(s), {columns.Length} column(s). "
                + (clipped ? "Long values or extra columns were clipped. " : "")
                + "Matched by table name; confirm relevance. Source freshness and whole-table coverage are unknown."));
        }

        var notice = sources.Count == 0
            ? "No usable matching project data was prepared. Connect a source in Data or add the context you want the agent to review."
            : "Review the proposed project sources before running. Matches use table names, not verified data mappings. "
                + "Only bounded row samples are included; freshness and whole-table coverage are unknown. No model has been called.";
        if (workflow.InputFields.Except(workflow.RequiredInputFields).Any(x => string.IsNullOrWhiteSpace(inputs[x])))
            notice += " Missing supporting sources will remain unknown in the analysis.";
        if (ambiguous) notice += " Some inputs matched multiple tables and were left unfilled; add the intended context.";
        if (unreadable) notice += " Some matching sources could not be read and were left unfilled.";
        if (omitted) notice += " Some sources did not fit the context limit and were left unfilled.";
        return Result(notice);

        AgentPreparation Result(string notice) => new(inputs, sources,
            workflow.RequiredInputFields.Where(x => !inputs.TryGetValue(x, out var value) || string.IsNullOrWhiteSpace(value)).ToArray(), notice);
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string Clip(string value, int limit)
    {
        if (value.Length <= limit) return value;
        if (char.IsHighSurrogate(value[limit - 1])) limit--;
        return value[..limit];
    }
}
