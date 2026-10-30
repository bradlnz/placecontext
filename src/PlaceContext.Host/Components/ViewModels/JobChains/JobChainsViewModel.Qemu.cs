using PlaceContext.Domain.ValueObjects;
using PlaceContext.Application.Ports;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public PlaceContext.Application.Ports.QemuEnvironmentCatalog QemuEnvironment { get; private set; } = new(false, []);
    public bool ShowQemuEditor { get; private set; }
    public string QemuImageProfile { get; set; } = "";
    public string QemuCommand { get; set; } = "";
    public string QemuOutputField { get; set; } = "qemu_result";
    public int QemuMemoryMb { get; set; } = 2048;
    public int QemuCpus { get; set; } = 1;
    public int QemuTimeoutSeconds { get; set; } = 300;
    public bool QemuAllowNetwork { get; set; }
    public bool QemuPauseForLogin { get; set; }
    public string QemuLoginId { get; set; } = "";
    public IReadOnlyList<QemuLoginProfile> QemuLogins { get; private set; } = [];
    public string? QemuLoginError { get; private set; }
    public async Task LoadQemuLoginsAsync()
    {
        var project = ProjectId;
        try
        {
            var profiles = _qemuLogins is null ? [] : await _qemuLogins.ListAsync(project);
            if (project != ProjectId) return;
            QemuLogins = profiles; QemuLoginError = null;
        }
        catch { if (project == ProjectId) QemuLoginError = "Could not load your saved logins."; }
        NotifyStateChanged();
    }
    public async Task ForgetQemuLoginAsync()
    {
        if (_qemuLogins is null || !Guid.TryParse(QemuLoginId, out var id)) return;
        try { await _qemuLogins.DeleteAsync(ProjectId, id); QemuLoginId = ""; await LoadQemuLoginsAsync(); }
        catch { QemuLoginError = "Could not delete the saved login."; NotifyStateChanged(); }
    }
    public string? QemuError { get; private set; }
    private int _qemuStageIndex;
    private bool _editingQemu;

    public void OpenQemuActionAt(int index)
    {
        QemuEnvironment = _qemuCatalog?.Get() ?? new(false, []);
        _qemuStageIndex = Math.Clamp(index, 0, EdStages.Count);
        _editingQemu = false;
        QemuImageProfile = QemuEnvironment.Images.FirstOrDefault(image => image.Available)?.Name ?? "";
        QemuCommand = "Start at https://example.com. Read the page and verify that its heading is Example Domain. Return the heading and final URL.";
        QemuOutputField = "qemu_result";
        QemuMemoryMb = 2048;
        QemuCpus = 1;
        QemuTimeoutSeconds = 300;
        QemuAllowNetwork = false;
        QemuPauseForLogin = false; QemuLoginId = ""; QemuLogins = [];
        QemuError = null;
        _ = LoadQemuLoginsAsync();
        _ = LoadQemuEnvironmentAsync();
        ShowQemuEditor = true;
        NotifyStateChanged();
    }

    private async Task LoadQemuEnvironmentAsync()
    {
        if (_qemuCatalog is null) return;
        var project = ProjectId;
        try
        {
            var environment = await _qemuCatalog.GetAsync(project);
            if (project == ProjectId) QemuEnvironment = environment;
        }
        catch { if (project == ProjectId) QemuError = "Could not load the project's QEMU configuration. Check its vault settings."; }
        NotifyStateChanged();
    }

    public void EditQemuAction(int index)
    {
        if (EdStageActions.GetValueOrDefault(index) is not QemuChainAction action) return;
        OpenQemuActionAt(index);
        _editingQemu = true;
        QemuImageProfile = action.ImageProfile;
        QemuCommand = action.Command;
        QemuOutputField = action.OutputField;
        QemuMemoryMb = action.MemoryMb;
        QemuCpus = action.Cpus;
        QemuTimeoutSeconds = action.TimeoutSeconds;
        QemuAllowNetwork = action.AllowNetwork;
        QemuPauseForLogin = action.PauseForLogin; QemuLoginId = action.LoginProfileId?.ToString() ?? "";
    }

    public void SaveQemuAction()
    {
        QemuError = null;
        try
        {
            if (!string.IsNullOrEmpty(QemuLoginId) && !Guid.TryParse(QemuLoginId, out _))
                throw new ArgumentException("Select a saved login or start with a fresh browser.");
            var action = new QemuChainAction(QemuImageProfile, QemuCommand, QemuOutputField,
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
        catch (ArgumentException ex) { QemuError = ex.Message; }
        NotifyStateChanged();
    }

    public async Task SaveQemuWorkflowAsync()
    {
        SaveQemuAction();
        if (QemuError is not null) return;
        await SaveChainAsync();
        if (EditorError is not null)
        {
            QemuError = EditorError;
            ShowQemuEditor = true;
            NotifyStateChanged();
        }
    }

    public void CloseQemuEditor() { ShowQemuEditor = false; NotifyStateChanged(); }
}
