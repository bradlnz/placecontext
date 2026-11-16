using System.Text.Json;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public string GateTruePathLabel => GateEditorStageIndex == EdStages.Count && Guid.TryParse(GateEditorTrueJobId, out var jobId) ? JobName(jobId) : EdStageActions.GetValueOrDefault(GateEditorStageIndex)?.DisplayName
        ?? (GateEditorStageIndex < EdStages.Count && EdStages[GateEditorStageIndex].Count > 0
            ? string.Join(" + ", EdStages[GateEditorStageIndex].Select(JobName)) : "Run this stage");
    public string GateFalsePathLabel => GateEditorElseStages.Count == 0 ? "Skip this stage"
        : string.Join(" → ", GateEditorElseStages.Select(stage => stage.Action?.DisplayName ?? string.Join(" + ", stage.JobIds.Select(JobName))));
    public sealed record ConditionField(string Path, string Type);
    public sealed record ConditionFieldOptions(IReadOnlyList<ConditionField> Fields, string? Notice = null);

    public async Task<ConditionFieldOptions> LoadConditionFieldsAsync(int stageIndex)
    {
        var fields = new Dictionary<string, ConditionField>(StringComparer.Ordinal);
        string? notice = null;
        var stages = EdStages.Take(stageIndex).Select((jobs, index) => (Jobs: jobs.ToArray(), Action: EdStageActions.GetValueOrDefault(index))).ToArray();
        foreach (var stage in stages)
        {
            // Parallel job outputs are arrays; gate paths currently support object properties only.
            if (stage.Jobs.Length == 1)
            {
                try
                {
                    var runs = await _svc.ListJobRunsAsync(stage.Jobs[0]);
                    var latest = runs.Where(run => run.Status is "Succeeded" or "Partial")
                        .OrderByDescending(run => run.StartedAt).FirstOrDefault();
                    if (latest is not null && LatestJsonOutput(await _svc.GetJobRunAsync(latest.Id)) is { } sample)
                    {
                        using var document = JsonDocument.Parse(sample);
                        AddFields(document.RootElement, "", 0);
                    }
                }
                catch (Exception)
                {
                    notice = "Some previous outputs could not be inspected. You can still enter a field path.";
                }
            }
            var action = stage.Action;
            var field = action switch
            {
                LoopChainAction loop => new ConditionField(loop.ResponseField,
                    loop.ResponseType == LoopChainAction.YesNoResponse ? "boolean" : loop.Type == LoopChainAction.HumanType ? "text" : "unknown"),
                ScoringGateChainAction score => new(score.OutputField, "object"),
                QueryDataChainAction query => new(query.OutputField, "object"),
                VirtualMachineChainAction vm => new(vm.OutputField, "object"),
                ContainerBuildChainAction build => new(build.OutputField, "object"),
                ContainerRunChainAction container => new(container.OutputField, "object"),
                ParallelWorkflowsChainAction parallel => new(parallel.OutputField, "array"),
                _ => null
            };
            if (field is null) continue;
            fields[field.Path] = field;
            if (action is ScoringGateChainAction)
            {
                Add(field.Path + ".passed", "boolean");
                Add(field.Path + ".score", "number");
                Add(field.Path + ".confidence", "number");
            }
            if (action is QueryDataChainAction)
            {
                Add(field.Path + ".rows", "array");
                Add(field.Path + ".row_count", "number");
                Add(field.Path + ".truncated", "boolean");
            }
        }
        if (EdStageGates.Any(entry => entry.Key < stageIndex && entry.Value is ConditionGate { ReviewQuestion: not null }))
        {
            Add("ai_gate_review.noul", "number");
            Add("ai_gate_review.path", "text");
            Add("ai_gate_review.question", "text");
            Add("ai_gate_review.model", "text");
        }
        return new(fields.Values.OrderBy(field => field.Path, StringComparer.Ordinal).ToArray(), notice);

        void Add(string path, string type) => fields[path] = new(path, type);
        void AddFields(JsonElement value, string path, int depth)
        {
            if (depth > 8 || fields.Count >= 200) return;
            if (path.Length > 0)
                Add(path, value.ValueKind switch
                {
                    JsonValueKind.True or JsonValueKind.False => "boolean",
                    JsonValueKind.Number => "number", JsonValueKind.Null => "null",
                    JsonValueKind.Object => "object", JsonValueKind.Array => "array", _ => "text"
                });
            if (value.ValueKind != JsonValueKind.Object) return;
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name.Contains('.') || property.Name.Contains(':')) continue;
                AddFields(property.Value, JoinPath(path, property.Name), depth + 1);
            }
        }
    }
}
