using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public sealed record WorkflowCatalogueNode(JobChainView Workflow, IReadOnlyList<WorkflowCatalogueNode> Children, bool IsReference);

    public IReadOnlyList<WorkflowCatalogueNode> WorkflowHierarchy
    {
        get
        {
            var workflows = Chains ?? [];
            var byId = workflows.ToDictionary(workflow => workflow.Id);
            var children = workflows.ToDictionary(workflow => workflow.Id,
                workflow => ReferencedWorkflows(workflow.Stages).Where(byId.ContainsKey).Distinct().ToArray());
            var referenced = children.Values.SelectMany(ids => ids).ToHashSet();
            var expanded = new HashSet<Guid>();
            var roots = new List<WorkflowCatalogueNode>();

            WorkflowCatalogueNode Visit(JobChainView workflow)
            {
                if (!expanded.Add(workflow.Id)) return new(workflow, [], true);
                return new(workflow, children[workflow.Id].Select(id => Visit(byId[id])).ToArray(), false);
            }

            foreach (var workflow in workflows.Where(workflow => !referenced.Contains(workflow.Id)))
                roots.Add(Visit(workflow));
            // Cyclic references must not hide workflows or recurse forever.
            foreach (var workflow in workflows.Where(workflow => !expanded.Contains(workflow.Id)))
                roots.Add(Visit(workflow));
            return roots;
        }
    }

    private static IEnumerable<Guid> ReferencedWorkflows(IEnumerable<JobChainStageView> stages)
    {
        foreach (var stage in stages)
        {
            if (stage.Action is SubWorkflowChainActionView child) yield return child.WorkflowId;
            if (stage.Action is ParallelWorkflowsChainActionView parallel)
                foreach (var branch in parallel.Branches) yield return branch.WorkflowId;
            foreach (var id in ReferencedWorkflows(stage.ElseBranch ?? [])) yield return id;
        }
    }
}
