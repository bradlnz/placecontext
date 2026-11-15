using PlaceContext.Domain.ValueObjects;
namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class JobChainsViewModel
{
    public bool ShowContainerEditor
    {
        get; private set;
    }
    public bool ContainerBuild
    {
        get; private set;
    }
    public string ContainerDockerfile { get; set; } = "";
    public string ContainerContextField { get; set; } = "files";
    public string ContainerImage { get; set; } = "";
    public string ContainerImageField { get; set; } = "container_build.image";
    public string ContainerOutputField { get; set; } = "container_run";
    public string ContainerCommand { get; set; } = "";
    public bool ContainerWeb
    {
        get; set;
    }
    public int ContainerPort { get; set; } = 8080;
    public int ContainerMemory { get; set; } = 512;
    public int ContainerCpus { get; set; } = 1;
    public int ContainerTimeout { get; set; } = 600;
    public bool ContainerNetwork
    {
        get; set;
    }
    public string? ContainerError
    {
        get; private set;
    }
    private int _containerStage;
    private bool _editingContainer;

    public void OpenContainerAction(int index, bool build, bool edit = false)
    {
        _containerStage = Math.Clamp(index, 0, EdStages.Count);
        _editingContainer = edit;
        ContainerBuild = build;
        ContainerDockerfile = "FROM python:3.13-alpine\nWORKDIR /app\nCOPY . .\nUSER 65534:65534\nCMD [\"python\", \"-m\", \"http.server\", \"8080\"]";
        ContainerContextField = "files";
        ContainerImage = "";
        ContainerImageField = "container_build.image";
        ContainerOutputField = build ? "container_build" : "container_run";
        ContainerCommand = "";
        ContainerWeb = false;
        ContainerPort = 8080;
        ContainerMemory = 512;
        ContainerCpus = 1;
        ContainerTimeout = 600;
        ContainerNetwork = false;
        if (edit && EdStageActions.GetValueOrDefault(index) is ContainerBuildChainAction b)
        {
            ContainerDockerfile = b.Dockerfile;
            ContainerContextField = b.ContextField;
            ContainerOutputField = b.OutputField;
            ContainerTimeout = b.TimeoutSeconds;
            ContainerNetwork = b.AllowNetwork;
        }
        if (edit && EdStageActions.GetValueOrDefault(index) is ContainerRunChainAction r)
        {
            ContainerImage = r.Image;
            ContainerImageField = r.ImageField;
            ContainerOutputField = r.OutputField;
            ContainerCommand = string.Join('\n', r.Command);
            ContainerWeb = r.Port is not null;
            ContainerPort = r.Port ?? 8080;
            ContainerMemory = r.MemoryMb;
            ContainerCpus = r.Cpus;
            ContainerTimeout = r.TimeoutSeconds;
            ContainerNetwork = r.AllowNetwork;
        }
        ContainerError = null;
        ShowContainerEditor = true;
        NotifyStateChanged();
    }
    public void EditContainerAction(int index) => OpenContainerAction(index, EdStageActions.GetValueOrDefault(index) is ContainerBuildChainAction, true);
    public void CloseContainerEditor()
    {
        ShowContainerEditor = false;
        NotifyStateChanged();
    }
    public void SaveContainerAction()
    {
        ContainerError = null;
        try
        {
            ChainAction action = ContainerBuild
                ? new ContainerBuildChainAction(ContainerDockerfile, ContainerContextField, ContainerOutputField, ContainerTimeout, ContainerNetwork)
                : new ContainerRunChainAction(ContainerImage, string.IsNullOrWhiteSpace(ContainerImage) ? ContainerImageField : "", ContainerOutputField,
                    string.IsNullOrEmpty(ContainerCommand) ? [] : ContainerCommand.Replace("\r\n", "\n").Split('\n'),
                    ContainerWeb ? ContainerPort : null, ContainerMemory, ContainerCpus, ContainerTimeout, ContainerNetwork);
            if (!_editingContainer)
            {
                ShiftGatesForInsert(_containerStage);
                ShiftActionsForInsert(_containerStage);
                EdStages.Insert(_containerStage, []);
            }
            EdStageActions[_containerStage] = action;
            _editingContainer = true;
            ShowContainerEditor = false;
        }
        catch (ArgumentException ex)
        {
            ContainerError = ex.Message;
        }
        NotifyStateChanged();
    }
}
