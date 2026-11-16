using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public PlaceContext.Application.Workload.VirtualMachines.VirtualMachineEnvironmentCatalog QemuEnvironment { get; private set; } = new(false, []);
    public bool ShowQemuEditor
    {
        get; private set;
    }
    public string VirtualMachineImageProfile { get; set; } = "";
    public string QemuCommand { get; set; } = "";
    public string QemuOutputField { get; set; } = "qemu_result";
    public int QemuMemoryMb { get; set; } = 2048;
    public int QemuCpus { get; set; } = 1;
    public int QemuTimeoutSeconds { get; set; } = 300;
    public bool QemuAllowNetwork
    {
        get; set;
    }
    public bool QemuPauseForLogin
    {
        get; set;
    }
    public string QemuLoginId { get; set; } = "";
    public IReadOnlyList<VirtualMachineLoginProfile> QemuLogins { get; private set; } = [];
    public string? QemuLoginError
    {
        get; private set;
    }
    public async Task LoadQemuLoginsAsync()
    {
        var project = ProjectId;
        try
        {
            var profiles = _qemuLogins is null ? [] : await _qemuLogins.ListAsync(project);
            if (project != ProjectId)
            {
                return;
            }
            QemuLogins = profiles;
            QemuLoginError = null;
        }
        catch
        {
            if (project == ProjectId)
            {
                QemuLoginError = "Could not load your saved logins.";
            }
        }
        NotifyStateChanged();
    }
    public async Task ForgetQemuLoginAsync()
    {
        if (_qemuLogins is null || !Guid.TryParse(QemuLoginId, out var id))
        {
            return;
        }
        try
        {
            await _qemuLogins.DeleteAsync(ProjectId, id);
            QemuLoginId = "";
            await LoadQemuLoginsAsync();
        }
        catch
        {
            QemuLoginError = "Could not delete the saved login.";
            NotifyStateChanged();
        }
    }
    public string? QemuError
    {
        get; private set;
    }
    public ChainRunView? QemuReplayRun { get; private set; }
    public int? QemuReplayStep { get; private set; }
    public bool ShowQemuRunModal { get; private set; }
    public string QemuRunTab { get; private set; } = "replay";
    public string QemuEditorTab { get; private set; } = "task";
    public VirtualMachineRunContext? QemuLiveContext => QemuReplayRun is { } run && QemuReplayStep is { } step
        ? new VirtualMachineRunContext(run.ProjectId, run.Id, step)
        : null;
    private int _qemuStageIndex;
    private bool _editingQemu;

    public bool OpenQemuReplay(ChainStepRunView step)
    {
        if (OpenRun is null || step.ActionType != VirtualMachineChainAction.ActionType)
        {
            return false;
        }
        QemuEnvironment = _qemuCatalog?.Get() ?? new(false, []);
        QemuReplayRun = OpenRun;
        QemuReplayStep = step.Index;
        QemuRunTab = "replay";
        ShowQemuRunModal = true;
        _ = LoadQemuEnvironmentAsync();
        NotifyStateChanged();
        return true;
    }

    public void OpenQemuActionAt(int index)
    {
        QemuEnvironment = _qemuCatalog?.Get() ?? new(false, []);
        _qemuStageIndex = Math.Clamp(index, 0, EdStages.Count);
        var replayRun = OpenRun ?? ChainRuns?.FirstOrDefault();
        QemuReplayRun = replayRun;
        QemuReplayStep = replayRun is null ? null : Enumerable.Range(0, _qemuStageIndex)
            .Sum(stage => EdStages[stage].Count == 0 ? 1 : EdStages[stage].Count);
        if (QemuReplayRun is not null && !QemuReplayRun.Steps.Any(step => step.Index == QemuReplayStep && step.ActionType == VirtualMachineChainAction.ActionType))
        {
            QemuReplayRun = null;
            QemuReplayStep = null;
        }
        _editingQemu = false;
        QemuEditorTab = "task";
        VirtualMachineImageProfile = QemuEnvironment.Images.FirstOrDefault(image => image.Available)?.Name ?? "";
        QemuCommand = "Start at https://example.com. Read the page and verify that its heading is Example Domain. Return the heading and final URL.";
        QemuOutputField = "qemu_result";
        QemuMemoryMb = 2048;
        QemuCpus = 1;
        QemuTimeoutSeconds = 300;
        QemuAllowNetwork = false;
        QemuPauseForLogin = false;
        QemuLoginId = "";
        QemuLogins = [];
        QemuError = null;
        _ = LoadQemuLoginsAsync();
        _ = LoadQemuEnvironmentAsync();
        ShowQemuEditor = true;
        NotifyStateChanged();
    }

    public void SetQemuRunTab(string tab)
    {
        QemuRunTab = tab.Equals("live", StringComparison.OrdinalIgnoreCase) ? "live" : "replay";
        NotifyStateChanged();
    }

    public void SetQemuEditorTab(string tab)
    {
        QemuEditorTab = tab.Equals("settings", StringComparison.OrdinalIgnoreCase) ? "settings" : "task";
        NotifyStateChanged();
    }

    private async Task LoadQemuEnvironmentAsync()
    {
        if (_qemuCatalog is null)
        {
            return;
        }
        var project = ProjectId;
        try
        {
            var environment = await _qemuCatalog.GetAsync(project);
            if (project == ProjectId)
            {
                QemuEnvironment = environment;
            }
        }
        catch
        {
            if (project == ProjectId)
            {
                QemuError = "Could not load the project's browser workspace configuration. Check its vault settings.";
            }
        }
        NotifyStateChanged();
    }

    public void EditQemuAction(int index)
    {
        if (EdStageActions.GetValueOrDefault(index) is not VirtualMachineChainAction action)
        {
            return;
        }
        OpenQemuActionAt(index);
        _editingQemu = true;
        VirtualMachineImageProfile = action.ImageProfile;
        QemuCommand = action.Command;
        QemuOutputField = action.OutputField;
        QemuMemoryMb = action.MemoryMb;
        QemuCpus = action.Cpus;
        QemuTimeoutSeconds = action.TimeoutSeconds;
        QemuAllowNetwork = action.AllowNetwork;
        QemuPauseForLogin = action.PauseForLogin;
        QemuLoginId = action.LoginProfileId?.ToString() ?? "";
    }

    public void SaveQemuAction()
    {
        QemuError = null;
        try
        {
            if (!string.IsNullOrEmpty(QemuLoginId) && !Guid.TryParse(QemuLoginId, out _))
            {
                throw new ArgumentException("Select a saved login or start with a fresh browser.");
            }
            var action = new VirtualMachineChainAction(VirtualMachineImageProfile, QemuCommand, QemuOutputField,
                            QemuMemoryMb, QemuCpus, QemuTimeoutSeconds, QemuAllowNetwork, QemuPauseForLogin,
                            string.IsNullOrEmpty(QemuLoginId) ? null : Guid.Parse(QemuLoginId));
            if (!_editingQemu)
            {
                ShiftGatesForInsert(_qemuStageIndex);
                ShiftActionsForInsert(_qemuStageIndex);
                EdStages.Insert(_qemuStageIndex, []);
            }
            EdStageActions[_qemuStageIndex] = action;
            _editingQemu = true;
            ShowQemuEditor = false;
        }
        catch (ArgumentException ex)
        {
            QemuError = ex.Message;
        }
        NotifyStateChanged();
    }

    public async Task SaveQemuWorkflowAsync()
    {
        SaveQemuAction();
        if (QemuError is not null)
        {
            return;
        }
        await SaveChainAsync();
        if (EditorError is not null)
        {
            QemuError = EditorError;
            ShowQemuEditor = true;
            NotifyStateChanged();
        }
    }

    public void CloseQemuEditor()
    {
        ShowQemuEditor = false;
        ShowQemuRunModal = false;
        QemuReplayRun = null;
        QemuReplayStep = null;
        NotifyStateChanged();
    }
}
