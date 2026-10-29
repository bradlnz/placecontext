using PlaceContext.Domain.ValueObjects;

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
    public string? QemuError { get; private set; }
    private int _qemuStageIndex;
    private bool _editingQemu;

    public void OpenQemuActionAt(int index)
    {
        QemuEnvironment = _qemuCatalog?.Get() ?? new(false, []);
        _qemuStageIndex = Math.Clamp(index, 0, EdStages.Count);
        _editingQemu = false;
        QemuImageProfile = QemuEnvironment.Images.FirstOrDefault(image => image.Available)?.Name ?? "";
        QemuCommand = "Open Chromium, inspect the desktop, and report when the browser is ready.";
        QemuOutputField = "qemu_result";
        QemuMemoryMb = 2048;
        QemuCpus = 1;
        QemuTimeoutSeconds = 300;
        QemuAllowNetwork = false;
        QemuError = null;
        ShowQemuEditor = true;
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
    }

    public void SaveQemuAction()
    {
        QemuError = null;
        try
        {
            var action = new QemuChainAction(QemuImageProfile, QemuCommand, QemuOutputField,
                QemuMemoryMb, QemuCpus, QemuTimeoutSeconds, QemuAllowNetwork);
            if (!_editingQemu)
            {
                ShiftGatesForInsert(_qemuStageIndex);
                ShiftActionsForInsert(_qemuStageIndex);
                EdStages.Insert(_qemuStageIndex, []);
            }
            EdStageActions[_qemuStageIndex] = action;
            ShowQemuEditor = false;
        }
        catch (ArgumentException ex) { QemuError = ex.Message; }
        NotifyStateChanged();
    }

    public void CloseQemuEditor() { ShowQemuEditor = false; NotifyStateChanged(); }
}
