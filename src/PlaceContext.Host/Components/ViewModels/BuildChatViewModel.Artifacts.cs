using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    public ArtifactFileView? PreviewArtifact { get; private set; }
    public bool ArtifactPreviewLoading { get; private set; }
    public string? ArtifactPreviewError { get; private set; }
    public IReadOnlyList<RunArtifactLinkView> PreviewRunArtifacts { get; private set; } = [];
    public string? RunArtifactsError { get; private set; }
    private int _artifactPreviewVersion;
    private Guid? _artifactOpenedForRun;

    public async Task OpenArtifactAsync(Guid artifactId, Guid? runId = null)
    {
        AgentOpen = false;
        CloseWorkflowPicker();
        OpenWorkspace();
        CloseArtifactPreview();
        var version = _artifactPreviewVersion;
        var projectId = Conversation.ProjectId;
        ArtifactPreviewLoading = true;
        NotifyStateChanged();
        try
        {
            if (artifacts is null || !await permissions.HasAsync(Permission.ProjectsView)
                || !await permissions.HasAsync(Permission.ArtifactsView)) throw new UnauthorizedAccessException();
            var artifact = await artifacts.GetAsync(artifactId);
            if (artifact is null || artifact.Id != artifactId || artifact.ProjectId != projectId
                || (runId is { } expected && artifact.RunId != expected)) throw new UnauthorizedAccessException();
            if (version == _artifactPreviewVersion) PreviewArtifact = artifact;
        }
        catch (Exception)
        {
            if (version == _artifactPreviewVersion)
                ArtifactPreviewError = "Could not open this artifact. It may have been removed or access is restricted.";
        }
        finally
        {
            if (version == _artifactPreviewVersion) ArtifactPreviewLoading = false;
            NotifyStateChanged();
        }
    }

    public void CloseArtifactPreview()
    {
        ++_artifactPreviewVersion;
        PreviewArtifact = null;
        ArtifactPreviewLoading = false;
        ArtifactPreviewError = null;
    }

    private async Task RefreshRunArtifactsAsync(ChainRunView run, int version, CancellationToken ct)
    {
        if (_artifactOpenedForRun == run.Id) return;
        try
        {
            if (!await permissions.HasAsync(Permission.ArtifactsView, ct)) return;
            var runIds = run.Steps.Select(s => s.RunId)
                .Concat(run.Steps.SelectMany(s => s.AgentActivity ?? []).Select(a => a.RunId))
                .OfType<Guid>().Distinct();
            var files = new List<RunArtifactLinkView>();
            foreach (var runId in runIds)
                files.AddRange((await service.ListRunArtifactsAsync(runId, ct)).Where(a => a.RunId == runId));
            if (version != _previewVersion || ct.IsCancellationRequested) return;
            PreviewRunArtifacts = files.DistinctBy(a => a.Id).OrderByDescending(a => a.CreatedAt).ToArray();
            RunArtifactsError = null;
            _artifactOpenedForRun = run.Id;
            if ((PreviewRunArtifacts.FirstOrDefault(a => ArtifactsViewModel.IsPdf(a.ContentType)) ?? PreviewRunArtifacts.FirstOrDefault()) is { } artifact)
                await OpenArtifactAsync(artifact.Id, artifact.RunId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            if (version == _previewVersion) RunArtifactsError = "Could not load the run's artifacts. Reopen the run to try again.";
        }
    }
}
