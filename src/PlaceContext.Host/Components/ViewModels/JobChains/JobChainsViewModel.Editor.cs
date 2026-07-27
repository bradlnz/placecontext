using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Host.Components.ViewModels.Helpers;
using PlaceContext.Infrastructure.Operations;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    // ── Editor state ──────────────────────────────────────────────────────────────────────────
    public bool ShowEditor { get; private set; }
    public bool Saving { get; private set; }
    public Guid? EditChainId { get; private set; }
    public string? EditorError { get; private set; }
    public string EditorTab { get; set; } = "details";
    public string EdName { get; set; } = "";
    public string EdDescription { get; set; } = "";
    public List<List<Guid>> EdStages { get; } = new();
    public string EdAddJobId { get; set; } = "";

    // ── Editor ────────────────────────────────────────────────────────────────────────────────
    public void NewChain()
    {
        EditChainId = null;
        EdName = "";
        EdDescription = "";
        EdStages.Clear();
        EdStages.Add(new List<Guid>());
        EdAddJobId = "";
        EditorError = null;
        EditorTab = "details";
        ShowEditor = true;
        NotifyStateChanged();
    }

    public async Task OpenChainAsync(JobChainView chain)
    {
        OpenChainEditor(chain);
        await SwitchToRunsTabAsync();
    }

    public void OpenChainEditor(JobChainView chain)
    {
        EditChainId = chain.Id;
        EdName = chain.Name;
        EdDescription = chain.Description ?? "";
        EdStages.Clear();
        foreach (var stage in chain.Stages)
            EdStages.Add(stage.Jobs.Select(j => j.JobId).ToList());
        if (EdStages.Count == 0) EdStages.Add(new List<Guid>());
        EdAddJobId = "";
        EditorError = null;
        EditorTab = "details";
        OpenRun = null;
        StepRunDetail = null;
        ShowEditor = true;
        NotifyStateChanged();
    }

    public void CloseEditor()
    {
        ShowEditor = false;
        EditorError = null;
        OpenRun = null;
        StepRunDetail = null;
        StopPolling();
        NotifyStateChanged();
    }

    public void AddStage()
    {
        if (Guid.TryParse(EdAddJobId, out var jobId))
            EdStages.Add(new List<Guid> { jobId });
        else
            EdStages.Add(new List<Guid>());
        NotifyStateChanged();
    }

    public void AddBranch(int stageIndex)
    {
        if (stageIndex >= 0 && stageIndex < EdStages.Count && Guid.TryParse(EdAddJobId, out var jobId))
        {
            EdStages[stageIndex].Add(jobId);
            NotifyStateChanged();
        }
    }

    public void RemoveBranch(int stageIndex, int branchIndex)
    {
        if (stageIndex >= 0 && stageIndex < EdStages.Count
            && branchIndex >= 0 && branchIndex < EdStages[stageIndex].Count)
        {
            EdStages[stageIndex].RemoveAt(branchIndex);
            if (EdStages[stageIndex].Count == 0 && EdStages.Count > 1)
                EdStages.RemoveAt(stageIndex);
            NotifyStateChanged();
        }
    }

    public void MoveStage(int index, int delta)
    {
        var target = index + delta;
        if (target < 0 || target >= EdStages.Count) return;
        (EdStages[index], EdStages[target]) = (EdStages[target], EdStages[index]);
        NotifyStateChanged();
    }

    public async Task SaveChainAsync()
    {
        EditorError = null;
        if (string.IsNullOrWhiteSpace(EdName)) { EditorError = "Name is required."; NotifyStateChanged(); return; }

        var stages = EdStages.Where(s => s.Count > 0).Select(s => (IReadOnlyList<Guid>)s.ToList()).ToList();
        if (stages.Count == 0) { EditorError = "Add at least one step."; NotifyStateChanged(); return; }
        var flatJobIds = EdStages.SelectMany(s => s).ToList();

        Saving = true;
        try
        {
            if (EditChainId.HasValue)
                await _svc.UpdateJobChainAsync(EditChainId.Value, EdName.Trim(),
                    string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(), flatJobIds, stages);
            else
                await _svc.CreateJobChainAsync(ProjectId, EdName.Trim(),
                    string.IsNullOrWhiteSpace(EdDescription) ? null : EdDescription.Trim(), flatJobIds, stages);

            Chains = await _svc.ListJobChainsAsync(ProjectId);
            ShowEditor = false;
            Message = EditChainId.HasValue ? $"Chain '{EdName.Trim()}' updated." : $"Chain '{EdName.Trim()}' created.";
        }
        catch (Exception ex) { EditorError = ex.Message; }
        finally { Saving = false; NotifyStateChanged(); }
    }

    public async Task DeleteChainAsync(Guid chainId)
    {
        try
        {
            await _svc.DeleteJobChainAsync(chainId);
            Chains = await _svc.ListJobChainsAsync(ProjectId);
            ConfirmDeleteId = null;
            if (EditChainId == chainId) CloseEditor();
            Message = "Chain deleted.";
        }
        catch (Exception ex) { Message = ex.Message; }
        NotifyStateChanged();
    }
}
