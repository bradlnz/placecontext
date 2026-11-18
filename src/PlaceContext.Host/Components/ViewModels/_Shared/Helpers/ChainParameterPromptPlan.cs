using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Resolves the parameterized execution steps for a workflow and prepares their stored input defaults.
/// The execution indexes match <c>RunJobChainCommand</c>, including action stages and parallel jobs.
/// </summary>
public sealed record ChainParameterPromptPlan(
    IReadOnlyList<(int Index, JobView Job)> Steps,
    IReadOnlyDictionary<string, string> Defaults
)
{
    public IReadOnlyList<(JobParameterDto Parameter, string Value)> Inputs { get; init; } = [];

    public static ChainParameterPromptPlan Build(JobChainView chain, IReadOnlyList<JobView> jobs,
        IReadOnlyList<JobChainView>? workflows = null)
    {
        var steps = new List<(int Index, JobView Job)>();
        var executionIndex = 0;
        foreach (var stage in chain.Stages)
        {
            if (stage.Action is not null)
            {
                executionIndex++;
                continue;
            }

            foreach (var chainStep in stage.Jobs)
            {
                var job = jobs.FirstOrDefault(item => item.Id == chainStep.JobId);
                if (job is { Parameters.Count: 0 } && stage == chain.Stages[0])
                    job = job with { Parameters = RunInputDefaults.Infer(job.InputPayloads) };
                if (job is { Parameters.Count: > 0 })
                {
                    steps.Add((executionIndex, job));
                }
                executionIndex++;
            }
        }

        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (index, job) in steps)
        {
            var stored = JsonPayloadHelper.FlattenScalars(job.InputPayloads, includeStructured: true);
            foreach (var parameter in job.Parameters)
            {
                var value = stored.GetValueOrDefault(parameter.Name, "");
                defaults[ParameterPromptState.ChainArgKey(index, parameter.Name)] =
                    string.IsNullOrWhiteSpace(value) ? RunInputDefaults.Generate(parameter) : value;
            }
        }

        var inputs = new Dictionary<string, (JobParameterDto Parameter, string Value)>(StringComparer.Ordinal);
        var visited = new HashSet<Guid> { chain.Id };
        void Visit(JobChainView workflow)
        {
            if (workflow.Stages.FirstOrDefault() is not { } first) return;
            IEnumerable<Guid> children = first.Action switch
            {
                SubWorkflowChainActionView child => [child.WorkflowId],
                ParallelWorkflowsChainActionView parallel => parallel.Branches.Select(branch => branch.WorkflowId),
                _ => []
            };
            foreach (var id in children)
            {
                if (!visited.Add(id)) continue;
                var child = workflows?.FirstOrDefault(item => item.Id == id && item.ProjectId == chain.ProjectId);
                if (child is null) continue;
                foreach (var entry in child.Stages.FirstOrDefault()?.Jobs ?? [])
                {
                    var job = jobs.FirstOrDefault(item => item.Id == entry.JobId);
                    if (job is null) continue;
                    var parameters = job.Parameters.Count > 0 ? job.Parameters : RunInputDefaults.Infer(job.InputPayloads);
                    var values = JsonPayloadHelper.FlattenScalars(job.InputPayloads, includeStructured: true);
                    foreach (var parameter in parameters)
                    {
                        var value = values.GetValueOrDefault(parameter.Name, "");
                        if (inputs.TryGetValue(parameter.Name, out var existing))
                        {
                            if (existing.Parameter.Type != parameter.Type)
                                throw new ArgumentException($"Sub-workflows use different types for input '{parameter.Name}'. Align their input definitions before running.");
                            inputs[parameter.Name] = (existing.Parameter with { Required = existing.Parameter.Required || parameter.Required }, existing.Value);
                        }
                        else inputs[parameter.Name] = (parameter, string.IsNullOrWhiteSpace(value) ? RunInputDefaults.Generate(parameter) : value);
                    }
                }
                Visit(child);
            }
        }
        Visit(chain);
        return new ChainParameterPromptPlan(steps, defaults) { Inputs = inputs.Values.ToArray() };
    }
}
