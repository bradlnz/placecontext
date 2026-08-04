using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class JobEditorViewModel : PageViewModel, IAsyncDisposable
{
    private const string CodeSourceKind = "code";
    private const string DefaultRuntime = JobTestRuntimeCatalog.Python;
    private const string EditorTheme = "vs-dark";
    private const string InitFunction = "pcmonaco.init";
    private const string GetValueFunction = "pcmonaco.getValue";
    private const string OpenFileFunction = "pcmonaco.openFile";
    private const string CloseFileFunction = "pcmonaco.closeFile";
    private const string DestroyFunction = "pcmonaco.destroy";

    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _js;
    private readonly List<EditorFile> _files = new();
    private JobView? _job;
    private int _active;
    private bool _monacoReady;
    private bool _saving;
    private bool _running;
    private bool _addingFile;
    private int _renamingIndex = -1;
    private string? _message;
    private string _newFileName = string.Empty;
    private string _renameValue = string.Empty;

    public JobEditorViewModel(
        IPlaceContextService service,
        PortalUiState ui,
        NavigationManager navigation,
        IJSRuntime js
    )
    {
        _service = service;
        _ui = ui;
        _navigation = navigation;
        _js = js;
        EditorId = $"pcmonaco-{Guid.NewGuid():N}";
    }

    public Guid ProjectId { get; private set; }
    public Guid JobId { get; private set; }
    public string EditorId { get; }
    public JobView? Job => _job;
    public IReadOnlyList<EditorFile> Files => _files;
    public int ActiveIndex => _active;
    public string? Entrypoint { get; private set; }
    public string RuntimeId { get; private set; } = DefaultRuntime;
    public JobRunDetailView? LastRun { get; private set; }
    public string? Message => _message;
    public bool Loading { get; private set; } = true;
    public bool Saving => _saving;
    public bool Running => _running;
    public bool Busy => _saving || _running;
    public bool IsCodeMap => _job?.MapSourceKind == CodeSourceKind;

    public string StatusTextColor(string status) => StatusColor(status);

    public string StatusTextBackground(string status) => StatusBackground(status);

    public string StatusLabelText(string status) => Presentation.StatusLabel(status);

    public string JsonText(string value) => Presentation.Json(value);

    public bool PanelOpen { get; set; }
    public bool MonacoLite { get; private set; }
    public bool AddingFile => _addingFile;
    public string NewFileName
    {
        get => _newFileName;
        set => _newFileName = value;
    }
    public int RenamingIndex => _renamingIndex;
    public string RenameValue
    {
        get => _renameValue;
        set => _renameValue = value;
    }
    public ElementReference NewFileInput { get; set; }
    public ElementReference RenameInput { get; set; }

    public void TogglePanel() => PanelOpen = !PanelOpen;

    public void Initialize(Guid projectId, Guid jobId)
    {
        ProjectId = projectId;
        JobId = jobId;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        Loading = true;
        _monacoReady = false;
        _message = null;
        try
        {
            _job = await _service.GetJobAsync(JobId);
            if (_job is { MapSourceKind: CodeSourceKind })
            {
                RuntimeId = _job.MapRuntimeId ?? DefaultRuntime;
                Entrypoint = _job.MapEntrypoint;
                _files.Clear();
                foreach (var file in _job.MapFiles)
                {
                    _files.Add(new EditorFile(file.Path, file.Content));
                }

                if (_files.Count == 0 && _job.MapSource is not null)
                {
                    _files.Add(new EditorFile(Entrypoint ?? "main", _job.MapSource));
                }

                _active = Math.Max(0, _files.FindIndex(file => file.Path == Entrypoint));
                _ui.Set(_job.Name, "code editor");
            }
        }
        catch (Exception exception)
        {
            _message = exception.Message;
        }
        finally
        {
            Loading = false;
            NotifyStateChanged();
        }
    }

    public async Task AfterRenderAsync()
    {
        if (_monacoReady || _job is not { MapSourceKind: CodeSourceKind } || _files.Count == 0)
        {
            return;
        }

        _monacoReady = true;
        var file = _files[_active];
        try
        {
            var rich = await _js.InvokeAsync<bool>(
                InitFunction,
                EditorId,
                file.Content,
                EditorLanguageCatalog.ForPath(file.Path),
                EditorTheme,
                file.Path
            );
            MonacoLite = !rich;
        }
        catch
        {
            MonacoLite = true;
        }

        NotifyStateChanged();
    }

    public void NavigateBack() => _navigation.NavigateTo($"/project/{ProjectId}/jobs");

    public async Task SwitchFileAsync(int index)
    {
        if (index < 0 || index >= _files.Count || index == _active)
        {
            return;
        }

        await SyncActiveAsync();
        _active = index;
        await OpenFileAsync(_files[_active]);
    }

    public async Task StartAddFile()
    {
        _addingFile = true;
        _newFileName = string.Empty;
        NotifyStateChanged();
        await Task.Yield();
        try
        {
            await NewFileInput.FocusAsync();
        }
        catch { }
    }

    public async Task AddFileKey(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await ConfirmAddFile();
        }
        else if (args.Key == "Escape")
        {
            _addingFile = false;
        }
    }

    public async Task ConfirmAddFile()
    {
        var path = EditorPathCatalog.Normalize(_newFileName);
        _message = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            _addingFile = false;
            return;
        }

        if (_files.Any(file => file.Path == path))
        {
            _message = $"'{path}' already exists.";
            return;
        }

        await SyncActiveAsync();
        _files.Add(new EditorFile(path, string.Empty));
        _active = _files.Count - 1;
        _addingFile = false;
        await OpenFileAsync(_files[_active]);
    }

    public async Task DeleteFileAsync(int index)
    {
        if (_files.Count <= 1 || index < 0 || index >= _files.Count)
        {
            return;
        }

        await SyncActiveAsync();
        var removed = _files[index];
        var wasActive = index == _active;
        _files.RemoveAt(index);
        if (removed.Path == Entrypoint)
        {
            Entrypoint = _files.Count == 1 ? null : _files[0].Path;
        }

        _active = _active > index ? _active - 1 : _active;
        _active = Math.Min(_active, _files.Count - 1);
        try
        {
            await _js.InvokeVoidAsync(CloseFileFunction, EditorId, removed.Path);
        }
        catch { }

        if (wasActive)
        {
            await OpenFileAsync(_files[_active]);
        }
    }

    public void SetEntry(string path) => Entrypoint = path;

    public async Task StartRenameAsync(int index)
    {
        if (index < 0 || index >= _files.Count)
        {
            return;
        }

        _renamingIndex = index;
        _renameValue = _files[index].Path;
        _message = null;
        NotifyStateChanged();
        await Task.Yield();
        try
        {
            await RenameInput.FocusAsync();
        }
        catch { }
    }

    public async Task RenameKey(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await ConfirmRenameAsync();
        }
        else if (args.Key == "Escape")
        {
            _renamingIndex = -1;
        }
    }

    public async Task ConfirmRenameAsync()
    {
        if (_renamingIndex < 0 || _renamingIndex >= _files.Count)
        {
            _renamingIndex = -1;
            return;
        }

        var index = _renamingIndex;
        var path = EditorPathCatalog.Normalize(_renameValue);
        var oldPath = _files[index].Path;
        if (string.IsNullOrWhiteSpace(path) || path == oldPath)
        {
            _renamingIndex = -1;
            return;
        }

        if (_files.Any(file => file.Path == path))
        {
            _message = $"'{path}' already exists.";
            return;
        }

        _files[index].Path = path;
        if (Entrypoint == oldPath)
        {
            Entrypoint = path;
        }

        _renamingIndex = -1;
        await SyncActiveAsync();
        try
        {
            await _js.InvokeVoidAsync(CloseFileFunction, EditorId, oldPath);
        }
        catch { }

        await OpenFileAsync(_files[index]);
    }

    public async Task DeployAsync() => await ExecuteSaveAsync("Deployed.");

    public async Task RunAsync()
    {
        if (_job is null)
        {
            return;
        }

        _running = true;
        _message = null;
        try
        {
            await SaveCoreAsync();
            LastRun = await _service.RunJobAsync(JobId);
            PanelOpen = true;
            _message = $"Run {LastRun.Status}.";
        }
        catch (Exception exception)
        {
            _message = exception.Message;
        }
        finally
        {
            _running = false;
            NotifyStateChanged();
        }
    }

    public static string StatusColor(string status) => JobEditorStatusCatalog.Color(status);

    public static string StatusBackground(string status) =>
        JobEditorStatusCatalog.Background(status);

    public static string PrettyJson(string raw)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(raw);
            return System.Text.Json.JsonSerializer.Serialize(
                document.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );
        }
        catch
        {
            return raw;
        }
    }

    private async Task ExecuteSaveAsync(string successMessage)
    {
        if (_job is null)
        {
            return;
        }

        _saving = true;
        _message = null;
        try
        {
            await SaveCoreAsync();
            _message = successMessage;
        }
        catch (Exception exception)
        {
            _message = exception.Message;
        }
        finally
        {
            _saving = false;
            NotifyStateChanged();
        }
    }

    private async Task SaveCoreAsync()
    {
        await SyncActiveAsync();
        var entrypoint = Entrypoint;
        if (entrypoint is null && _files.Count > 1)
        {
            entrypoint = _files[0].Path;
        }

        _job = await _service.UploadJobCodeAsync(
            new UploadJobCodeCommand(
                JobId,
                ProjectId,
                _job!.Name,
                RuntimeId,
                entrypoint,
                _files.Select(file => new CodeFileDto(file.Path, file.Content)).ToList()
            )
        );
        Entrypoint = _job.MapEntrypoint ?? entrypoint;
    }

    private async Task SyncActiveAsync()
    {
        if (!_monacoReady || _files.Count == 0)
        {
            return;
        }

        try
        {
            var content = await _js.InvokeAsync<string?>(GetValueFunction, EditorId);
            if (content is not null)
            {
                _files[_active].Content = content;
            }
        }
        catch { }
    }

    private async Task OpenFileAsync(EditorFile file)
    {
        try
        {
            await _js.InvokeVoidAsync(
                OpenFileFunction,
                EditorId,
                file.Path,
                file.Content,
                EditorLanguageCatalog.ForPath(file.Path)
            );
        }
        catch (Exception exception)
        {
            _message = exception.Message;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _js.InvokeVoidAsync(DestroyFunction, EditorId);
        }
        catch { }
    }

    public sealed class EditorFile
    {
        public EditorFile(string path, string content)
        {
            Path = path;
            Content = content;
        }

        public string Path { get; set; }
        public string Content { get; set; }
    }
}
