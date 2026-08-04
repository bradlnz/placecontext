using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Resolves the parameterized execution steps for a chain and prepares their stored input defaults.
/// The execution indexes match <c>RunJobChainCommand</c>, including action stages and parallel jobs.
/// </summary>
public sealed record ChainParameterPromptPlan(
    IReadOnlyList<(int Index, JobView Job)> Steps,
    IReadOnlyDictionary<string, string> Defaults
)
{
    public static ChainParameterPromptPlan Build(JobChainView chain, IReadOnlyList<JobView> jobs)
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
                if (job is { Parameters.Count: > 0 })
                    steps.Add((executionIndex, job));
                executionIndex++;
            }
        }

        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (index, job) in steps)
        {
            var stored = JsonPayloadHelper.FlattenScalars(job.InputPayloads);
            foreach (var parameter in job.Parameters)
            {
                defaults[ParameterPromptState.ChainArgKey(index, parameter.Name)] =
                    stored.GetValueOrDefault(parameter.Name, "");
            }
        }

        return new ChainParameterPromptPlan(steps, defaults);
    }
}
