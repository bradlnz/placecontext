using System.Text.Json;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class BuildChatViewModel
{
    private readonly ParameterPromptState _humanInput = new();
    private Guid? _humanInputRequestId;
    public ChainInputRequest? HumanInputRequest => PreviewRun is { Status: "AwaitingInput",
        InputRequest: { Kind: LoopChainAction.HumanType, SubmittedValues: null } request } ? request : null;
    public bool CanRespondToWorkflow { get; private set; }
    public bool SubmittingHumanInput { get; private set; }
    public bool? HumanAnswer { get; private set; }
    public string? HumanInputError { get; private set; }
    public string? HumanInputNotice { get; private set; }

    private void SyncHumanInput()
    {
        var request = HumanInputRequest;
        if (_humanInputRequestId == request?.Id) return;
        _humanInputRequestId = request?.Id;
        _humanInput.Clear();
        HumanAnswer = null;
        HumanInputError = null;
        if (request is null) return;
        HumanInputNotice = null;
        foreach (var step in request.Steps)
            foreach (var field in step.Parameters)
                _humanInput.Set($"{step.Index}:{field.Name}", step.Values?.GetValueOrDefault(field.Name)?.ToString() ?? "");
    }

    public string GetHumanInput(int index, string name) => _humanInput.Get($"{index}:{name}");
    public void SetHumanInput(int index, string name, string value) => _humanInput.Set($"{index}:{name}", value);

    public void SelectHumanAnswer(Guid requestId, bool? answer)
    {
        if (SubmittingHumanInput || !CanRespondToWorkflow || HumanInputRequest?.Id != requestId) return;
        HumanAnswer = answer;
        HumanInputError = null;
    }

    public async Task SubmitHumanInputAsync(Guid requestId)
    {
        if (SubmittingHumanInput || submissions is null || HumanInputRequest is not { } request
            || request.Id != requestId || PreviewRun is not { } run || PreviewError is not null) return;
        var version = _previewVersion;
        SubmittingHumanInput = true;
        HumanInputError = null;
        NotifyStateChanged();
        try
        {
            if (!await permissions.HasAsync(Permission.ProjectsView) || !await permissions.HasAsync(Permission.ChainsManage)
                || !await permissions.HasAsync(Permission.JobsRun))
                throw new UnauthorizedAccessException("Permission to run workflows is required to respond.");
            if (version != _previewVersion || HumanInputRequest?.Id != requestId) return;
            if (request.ResponseType == LoopChainAction.YesNoResponse && HumanAnswer is null)
                throw new ArgumentException("Choose Yes or No to continue.");
            if (request.ResponseType != LoopChainAction.YesNoResponse && !_humanInput.ValidateRequired(request.Steps.SelectMany(step =>
                step.Parameters.Select(field => ($"{step.Index}:{field.Name}", field.Label ?? "Your response", field.Required)))))
                throw new ArgumentException(_humanInput.Error);
            var values = request.Steps.ToDictionary(step => step.Index.ToString(), step =>
                request.ResponseType == LoopChainAction.YesNoResponse
                    ? JsonSerializer.SerializeToElement(step.Parameters.ToDictionary(field => field.Name, _ => HumanAnswer!.Value))
                    : JsonSerializer.Deserialize<JsonElement>(ParameterPromptState.ToJsonPayload(step.Parameters,
                        name => GetHumanInput(step.Index, name))));
            var payload = JsonSerializer.Serialize(values);
            var status = await submissions.ResumeAsync(run.Id, request.Id, payload);
            if (version != _previewVersion) return;
            PreviewRun = run with { Status = status, InputRequest = request with { SubmittedValues = payload } };
            HumanInputNotice = "Response submitted. The workflow is continuing.";
            SyncHumanInput();
            await RefreshPreviewRunAsync();
        }
        catch (Exception ex)
        {
            if (version == _previewVersion) HumanInputError = ex.Message;
        }
        finally
        {
            if (version == _previewVersion) SubmittingHumanInput = false;
            NotifyStateChanged();
        }
    }
}
