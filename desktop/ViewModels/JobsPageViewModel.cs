using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlaceContext.Desktop.Models;

namespace PlaceContext.Desktop.ViewModels;

public partial class EditableJobFile(string path, string content) : ObservableObject
{
    [ObservableProperty] public partial string Path { get; set; } = path;
    [ObservableProperty] public partial string Content { get; set; } = content;
}

public partial class JobsPageViewModel : PageViewModel
{
    private readonly Func<Guid, Guid, Task<DesktopJobDetail>> _load;
    private readonly Func<DesktopJobDetail, bool, Task<DesktopJobDetail>> _save;
    private readonly Func<Guid, Guid, string?, Task<DesktopActionResponse>> _run;
    private IReadOnlyList<CoreJob> _allJobs = [];
    private DesktopJobDetail? _loaded;

    public JobsPageViewModel(
        Func<Guid, Guid, Task<DesktopJobDetail>> load,
        Func<DesktopJobDetail, bool, Task<DesktopJobDetail>> save,
        Func<Guid, Guid, string?, Task<DesktopActionResponse>> run)
        : base("Jobs", "Edit, deploy, and run workspace jobs with native controls")
    {
        _load = load;
        _save = save;
        _run = run;
    }

    public ObservableCollection<CoreProject> Projects { get; } = [];
    public ObservableCollection<CoreJob> Jobs { get; } = [];
    public ObservableCollection<EditableJobFile> Files { get; } = [];
    public ObservableCollection<RunResultLine> RunResults { get; } = [];
    public IReadOnlyList<string> Runtimes { get; } = ["node", "python", "go", "ruby", "dotnet"];
    public IReadOnlyList<string> ReturnTypes { get; } = ["Json", "Table", "Chart", "Html", "Csv", "Text", "Pdf", "Image", "Video"];

    [ObservableProperty] public partial CoreProject? SelectedProject { get; set; }
    [ObservableProperty] public partial CoreJob? SelectedJob { get; set; }
    [ObservableProperty] public partial EditableJobFile? SelectedFile { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    [ObservableProperty] public partial string RuntimeId { get; set; } = "python";
    [ObservableProperty] public partial string Entrypoint { get; set; } = "main.py";
    [ObservableProperty] public partial string MapImage { get; set; } = string.Empty;
    [ObservableProperty] public partial string InputPayloads { get; set; } = "{}";
    [ObservableProperty] public partial string Environment { get; set; } = string.Empty;
    [ObservableProperty] public partial string SuccessExitCodes { get; set; } = "0";
    [ObservableProperty] public partial string PartialExitCodes { get; set; } = string.Empty;
    [ObservableProperty] public partial int ConcurrencyLimit { get; set; } = 4;
    [ObservableProperty] public partial int RetryCount { get; set; }
    [ObservableProperty] public partial int RetryDelaySeconds { get; set; }
    [ObservableProperty] public partial bool AllowNetworkEgress { get; set; }
    [ObservableProperty] public partial bool AllowApiInvocation { get; set; }
    [ObservableProperty] public partial string ReturnType { get; set; } = "Json";
    [ObservableProperty] public partial string ReturnFileName { get; set; } = string.Empty;
    [ObservableProperty] public partial string RunInput { get; set; } = "{}";
    [ObservableProperty] public partial string Status { get; set; } = "Select a job to open its editor.";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool HasDefinition { get; set; }
    [ObservableProperty] public partial bool IsCodeJob { get; set; }
    [ObservableProperty] public partial bool IsNew { get; set; }
    [ObservableProperty] public partial bool HasRunResults { get; set; }

    partial void OnSelectedProjectChanged(CoreProject? value)
    {
        FilterJobs();
        NewJobCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedJobChanged(CoreJob? value)
    {
        if (value is not null) _ = LoadAsync(value.ProjectId, value.Id);
    }

    partial void OnIsBusyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
        AddFileCommand.NotifyCanExecuteChanged();
        DeleteFileCommand.NotifyCanExecuteChanged();
        NewJobCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasDefinitionChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCodeJobChanged(bool value) => AddFileCommand.NotifyCanExecuteChanged();
    partial void OnSelectedFileChanged(EditableJobFile? value) => DeleteFileCommand.NotifyCanExecuteChanged();

    public void Update(WorkspaceSnapshot snapshot)
    {
        var projectId = SelectedProject?.Id;
        var jobId = SelectedJob?.Id;
        Projects.Clear();
        foreach (var project in snapshot.Projects) Projects.Add(project);
        _allJobs = snapshot.Jobs;
        SelectedProject = Projects.FirstOrDefault(project => project.Id == projectId) ?? Projects.FirstOrDefault();
        FilterJobs();
        SelectedJob = Jobs.FirstOrDefault(job => job.Id == jobId) ?? SelectedJob;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void NewJob()
    {
        if (SelectedProject is null) return;
        SelectedJob = null;
        var file = new DesktopJobFile("main.py", "import json, sys\n\npayload = json.loads(sys.stdin.read() or \"{}\")\nprint(json.dumps({\"ok\": True, \"input\": payload}))\n");
        Apply(new DesktopJobDetail(
            Guid.Empty, SelectedProject.Id, "New job", null, "code", null, "python", file.Content,
            file.Path, [file], ["{}"], new Dictionary<string, string>(), null, null, null, null, null,
            [], null, 4, [0], [], false, false, [], [], "Json", null, 0, 0, [],
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        IsNew = true;
        Status = "New native job definition.";
    }

    private bool CanCreate() => !IsBusy && SelectedProject is not null;

    [RelayCommand(CanExecute = nameof(CanEditFiles))]
    private void AddFile()
    {
        var extension = RuntimeId switch
        {
            "python" => ".py", "node" => ".js", "go" => ".go", "dotnet" => ".cs",
            "ruby" => ".rb", _ => ".txt",
        };
        var index = 1;
        string path;
        do path = $"file{index++}{extension}"; while (Files.Any(file => file.Path == path));
        var created = new EditableJobFile(path, string.Empty);
        Files.Add(created);
        SelectedFile = created;
        DeleteFileCommand.NotifyCanExecuteChanged();
    }

    private bool CanEditFiles() => !IsBusy && HasDefinition && IsCodeJob;

    [RelayCommand(CanExecute = nameof(CanDeleteFile))]
    private void DeleteFile()
    {
        if (SelectedFile is null || Files.Count <= 1) return;
        var index = Files.IndexOf(SelectedFile);
        Files.Remove(SelectedFile);
        SelectedFile = Files[Math.Clamp(index, 0, Files.Count - 1)];
        DeleteFileCommand.NotifyCanExecuteChanged();
    }

    private bool CanDeleteFile() => CanEditFiles() && SelectedFile is not null && Files.Count > 1;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync() => await SaveCoreAsync();

    private bool CanSave() => !IsBusy && HasDefinition;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var saved = await SaveCoreAsync();
        if (saved is null) return;
        IsBusy = true;
        Status = "Starting job run…";
        try
        {
            var result = await _run(saved.ProjectId, saved.Id,
                string.IsNullOrWhiteSpace(RunInput) ? null : RunInput);
            Status = $"{result.Message} Status: {result.Status}.";
            RunResults.Clear();
            foreach (var shard in result.Shards ?? [])
                RunResults.Add(new RunResultLine(
                    $"Shard {shard.Index} · {shard.Outcome} · exit {shard.ExitCode}",
                    shard.Artifact ?? "No primary output",
                    shard.Log ?? string.Empty));
            HasRunResults = RunResults.Count > 0;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            Status = $"Run failed · {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRun() => CanSave();

    private async Task<DesktopJobDetail?> SaveCoreAsync()
    {
        if (_loaded is null) return null;
        if (string.IsNullOrWhiteSpace(Name))
        {
            Status = "Save failed · Name is required.";
            return null;
        }
        if (IsCodeJob && (Files.Count == 0 || Files.Any(file => string.IsNullOrWhiteSpace(file.Path))))
        {
            Status = "Save failed · Every code file needs a path.";
            return null;
        }

        try
        {
            var payloads = InputPayloads.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (payloads.Length == 0) throw new ArgumentException("At least one input payload is required.");
            var env = ParseEnvironment(Environment);
            var success = ParseExitCodes(SuccessExitCodes, "success");
            var partial = ParseExitCodes(PartialExitCodes, "partial");
            var files = Files.Select(file => new DesktopJobFile(file.Path.Trim(), file.Content)).ToList();
            var entry = IsCodeJob
                ? (files.FirstOrDefault(file => file.Path == Entrypoint)?.Path ?? files[0].Path)
                : null;
            var source = IsCodeJob ? files.First(file => file.Path == entry).Content : null;
            var candidate = _loaded with
            {
                Name = Name.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                MapImage = IsCodeJob ? null : MapImage.Trim(),
                MapRuntimeId = IsCodeJob ? RuntimeId : null,
                MapSource = source,
                MapEntrypoint = entry,
                MapFiles = IsCodeJob ? files : [],
                InputPayloads = payloads,
                MapEnv = env,
                ConcurrencyLimit = Math.Max(1, ConcurrencyLimit),
                SuccessExitCodes = success,
                PartialExitCodes = partial,
                AllowNetworkEgress = AllowNetworkEgress,
                AllowApiInvocation = AllowApiInvocation,
                ReturnType = ReturnType,
                ReturnFileName = string.IsNullOrWhiteSpace(ReturnFileName) ? null : ReturnFileName.Trim(),
                RetryCount = Math.Max(0, RetryCount),
                RetryDelaySeconds = Math.Max(0, RetryDelaySeconds),
            };

            IsBusy = true;
            Status = IsNew ? "Creating job…" : "Deploying changes…";
            var saved = await _save(candidate, IsNew);
            Apply(saved);
            IsNew = false;
            Status = $"Job '{saved.Name}' deployed.";
            return saved;
        }
        catch (Exception exception) when (exception is ArgumentException or HttpRequestException or OperationCanceledException)
        {
            Status = $"Save failed · {exception.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync(Guid projectId, Guid jobId)
    {
        IsBusy = true;
        HasDefinition = false;
        Status = "Loading job definition…";
        try
        {
            Apply(await _load(projectId, jobId));
            IsNew = false;
            Status = "Job definition loaded.";
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            Status = $"Job failed to load · {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(DesktopJobDetail job)
    {
        _loaded = job;
        Name = job.Name;
        Description = job.Description ?? string.Empty;
        IsCodeJob = job.MapSourceKind.Equals("code", StringComparison.OrdinalIgnoreCase);
        RuntimeId = job.MapRuntimeId ?? "python";
        MapImage = job.MapImage ?? string.Empty;
        Entrypoint = job.MapEntrypoint ?? DefaultEntrypoint(RuntimeId);
        InputPayloads = string.Join(System.Environment.NewLine, job.InputPayloads);
        Environment = string.Join(System.Environment.NewLine, job.MapEnv.Select(pair => $"{pair.Key}={pair.Value}"));
        SuccessExitCodes = string.Join(", ", job.SuccessExitCodes);
        PartialExitCodes = string.Join(", ", job.PartialExitCodes);
        ConcurrencyLimit = job.ConcurrencyLimit;
        RetryCount = job.RetryCount;
        RetryDelaySeconds = job.RetryDelaySeconds;
        AllowNetworkEgress = job.AllowNetworkEgress;
        AllowApiInvocation = job.AllowApiInvocation;
        ReturnType = job.ReturnType;
        ReturnFileName = job.ReturnFileName ?? string.Empty;
        RunInput = job.InputPayloads.FirstOrDefault() ?? "{}";
        RunResults.Clear();
        HasRunResults = false;
        Files.Clear();
        var sourceFiles = job.MapFiles.Count > 0
            ? job.MapFiles
            : IsCodeJob ? [new DesktopJobFile(Entrypoint, job.MapSource ?? string.Empty)] : [];
        foreach (var file in sourceFiles) Files.Add(new EditableJobFile(file.Path, file.Content));
        SelectedFile = Files.FirstOrDefault(file => file.Path == Entrypoint) ?? Files.FirstOrDefault();
        HasDefinition = true;
    }

    private void FilterJobs()
    {
        var selectedId = SelectedJob?.Id;
        Jobs.Clear();
        if (SelectedProject is { } project)
            foreach (var job in _allJobs.Where(job => job.ProjectId == project.Id)) Jobs.Add(job);
        SelectedJob = Jobs.FirstOrDefault(job => job.Id == selectedId);
    }

    private static Dictionary<string, string> ParseEnvironment(string value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) throw new ArgumentException($"Environment entry '{line}' must use NAME=value.");
            result[line[..separator].Trim()] = line[(separator + 1)..];
        }
        return result;
    }

    private static int[] ParseExitCodes(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var parts = value.Split([',', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Any(part => !int.TryParse(part, out _)))
            throw new ArgumentException($"The {label} exit codes must be integers.");
        return parts.Select(int.Parse).Distinct().ToArray();
    }

    private static string DefaultEntrypoint(string runtime) => runtime switch
    {
        "python" => "main.py", "node" => "index.js", "go" => "main.go", "dotnet" => "main.cs",
        "ruby" => "main.rb", _ => "index.js",
    };
}
