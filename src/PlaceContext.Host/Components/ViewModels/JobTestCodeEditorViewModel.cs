using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;

namespace PlaceContext.Host.Components.ViewModels;

public sealed class JobTestCodeEditorViewModel : PageViewModel, IAsyncDisposable
{
    private const string EditorTheme = "vs-dark";
    private const string DefaultRuntime = JobTestRuntimeCatalog.Default;
    private const string InitFunction = "pcmonaco.init";
    private const string GetValueFunction = "pcmonaco.getValue";
    private const string OpenFileFunction = "pcmonaco.openFile";
    private const string CloseFileFunction = "pcmonaco.closeFile";
    private const string DestroyFunction = "pcmonaco.destroy";
    private const string NotRunStatus = "NotRun";

    private readonly IPlaceContextService _service;
    private readonly PortalUiState _ui;
    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _js;
    private readonly List<EditorFile> _files = new();
    private JobTestCaseView? _test;
    private int _active;
    private bool _monacoReady;
    private bool _saving;
    private bool _running;
    private bool _addingFile;
    private string _newFileName = string.Empty;
    private string? _message;

    public JobTestCodeEditorViewModel(
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
        EditorId = $"pctestmonaco-{Guid.NewGuid():N}";
    }

    public Guid ProjectId { get; private set; }
    public Guid TestId { get; private set; }
    public string EditorId { get; }
    public JobTestCaseView? Test => _test;
    public JobTestCaseView? LastResult { get; private set; }
    public IReadOnlyList<EditorFile> Files => _files;
    public string RuntimeId { get; private set; } = DefaultRuntime;
    public string? Entrypoint { get; private set; }
    public string? Message => _message;
    public string DefaultEntrypoint => JobTestRuntimeCatalog.DefaultEntrypoint(RuntimeId);
    public int ActiveIndex => _active;
    public bool Loading { get; private set; } = true;
    public bool Saving => _saving;
    public bool Running => _running;
    public bool Busy => _saving || _running;
    public bool PanelOpen { get; set; } = true;
    public bool MonacoLite { get; private set; }
    public bool AddingFile => _addingFile;
    public string NewFileName
    {
        get => _newFileName;
        set => _newFileName = value;
    }
    public ElementReference NewFileInput { get; set; }

    public void TogglePanel() => PanelOpen = !PanelOpen;

    public Task RuntimeChanged(ChangeEventArgs args)
    {
        RuntimeId = args.Value?.ToString() ?? DefaultRuntime;
        _ui.Set(_test?.Name ?? "Test code", $"test code · {RuntimeId}");
        _message = "Runtime changed. Use Starter to replace the files with a matching example.";
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public IReadOnlyList<JobTestMethodResult> Methods =>
        LastResult?.MethodResults is { Count: > 0 } results
            ? results
            : JobTestFramework.Discover(
                RuntimeId,
                _files.Select(file => new CodeFileDto(file.Path, file.Content)).ToList()
            );

    public void Initialize(Guid projectId, Guid testId)
    {
        ProjectId = projectId;
        TestId = testId;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        Loading = true;
        _monacoReady = false;
        try
        {
            _test = await _service.GetJobTestCaseAsync(TestId);
            if (_test is not null && _test.ProjectId != ProjectId)
            {
                _test = null;
            }
            else if (_test is not null)
            {
                RuntimeId = _test.RuntimeId ?? DefaultRuntime;
                Entrypoint = _test.Entrypoint;
                _files.Clear();
                _files.AddRange(
                    _test.CodeFiles.Select(file => new EditorFile(file.Path, file.Content))
                );
                if (_files.Count == 0)
                {
                    ApplyStarter(RuntimeId);
                }

                _active = Math.Max(0, _files.FindIndex(file => file.Path == Entrypoint));
                LastResult = _test.LastStatus == NotRunStatus ? null : _test;
                _ui.Set(_test.Name, $"test code · {RuntimeId}");
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
        if (_monacoReady || _test is null || _files.Count == 0)
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

    public void NavigateBack() => _navigation.NavigateTo($"/project/{ProjectId}/tests");

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
        _files.RemoveAt(index);
        if (Entrypoint == removed.Path)
        {
            Entrypoint = _files[0].Path;
        }

        _active = Math.Clamp(_active > index ? _active - 1 : _active, 0, _files.Count - 1);
        try
        {
            await _js.InvokeVoidAsync(CloseFileFunction, EditorId, removed.Path);
        }
        catch { }

        await OpenFileAsync(_files[_active]);
    }

    public void SetEntry(string path) => Entrypoint = path;

    public async Task ResetStarterAsync()
    {
        await SyncActiveAsync();
        foreach (var file in _files)
        {
            try
            {
                await _js.InvokeVoidAsync(CloseFileFunction, EditorId, file.Path);
            }
            catch { }
        }

        ApplyStarter(RuntimeId);
        await OpenFileAsync(_files[0]);
        _message = $"{JobTestRuntimeCatalog.Label(RuntimeId)} starter loaded. Save to keep it.";
    }

    public async Task SaveAsync() => await ExecuteAsync("Test code saved.", false);

    public async Task RunAsync() => await ExecuteAsync(null, true);

    public static string RuntimeLabel(string runtime) => JobTestRuntimeCatalog.Label(runtime);

    public static string MethodIcon(string status) =>
        status switch
        {
            "Passed" => "✓",
            "Failed" => "×",
            "Skipped" => "–",
            _ => "○",
        };

    public static string FormatDuration(long milliseconds) =>
        milliseconds < 1000 ? $"{milliseconds} ms" : $"{milliseconds / 1000d:0.0} s";

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

    private void ApplyStarter(string runtime)
    {
        var starter = JobTestRuntimeCatalog.Starter(runtime);
        _files.Clear();
        _files.Add(new EditorFile(starter.Path, starter.Content));
        if (runtime == JobTestRuntimeCatalog.Go)
        {
            _files.Add(new EditorFile("go.mod", "module placecontext_tests\n\ngo 1.23\n"));
        }
        else if (runtime == JobTestRuntimeCatalog.Python)
        {
            _files.Add(new EditorFile("requirements.txt", "pytest==8.4.1\n"));
        }

        Entrypoint = starter.Path;
        _active = 0;
    }

    private async Task ExecuteAsync(string? successMessage, bool run)
    {
        if (_test is null)
        {
            return;
        }

        _saving = !run;
        _running = run;
        _message = null;
        try
        {
            await SaveCoreAsync();
            if (run)
            {
                LastResult = await _service.RunJobTestCaseAsync(TestId);
                _test = LastResult;
                PanelOpen = true;
                _message = LastResult.LastStatus;
            }
            else
            {
                _message = successMessage;
            }
        }
        catch (Exception exception)
        {
            _message = exception.Message;
        }
        finally
        {
            _saving = false;
            _running = false;
            NotifyStateChanged();
        }
    }

    private async Task<JobTestCaseView> SaveCoreAsync()
    {
        await SyncActiveAsync();
        var entrypoint = string.IsNullOrWhiteSpace(Entrypoint) ? _files[0].Path : Entrypoint;
        var updated = await _service.UpdateJobTestCodeAsync(
            new UpdateJobTestCodeCommand(
                TestId,
                RuntimeId,
                entrypoint,
                _files.Select(file => new CodeFileDto(file.Path, file.Content)).ToList(),
                false
            )
        );
        _test = updated;
        Entrypoint = updated.Entrypoint ?? entrypoint;
        LastResult = updated.LastStatus == NotRunStatus ? null : updated;
        return updated;
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
