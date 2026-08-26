using PlaceContext.Desktop.Models;
using PlaceContext.Desktop.ViewModels;

namespace PlaceContext.Desktop.Tests;

public sealed class JobsPageViewModelTests
{
    [Fact]
    public async Task Editing_code_saves_all_files_and_preserves_advanced_configuration()
    {
        var project = new CoreProject(Guid.NewGuid(), "Production", "/srv/production", "Ready", true);
        var job = new CoreJob(Guid.NewGuid(), project.Id, "Deploy", "Release", "code", "Json", false, false, DateTimeOffset.UtcNow);
        var mcpId = Guid.NewGuid();
        var definition = new DesktopJobDetail(
            job.Id, project.Id, job.Name, job.Description, "code", null, "python", "print('old')", "main.py",
            [new DesktopJobFile("main.py", "print('old')"), new DesktopJobFile("helpers.py", "VALUE = 1")],
            ["{}"], new Dictionary<string, string> { ["MODE"] = "safe" },
            "code", null, "python", "print('reduce')", "reduce.py",
            [new DesktopJobFile("reduce.py", "print('reduce')")], new Dictionary<string, string> { ["REDUCE"] = "1" },
            4, [0], [2], true, true,
            [new DesktopJobParameter("target", "Target", true, "text", null)],
            ["HtmlReport"], "Json", null, 2, 5, [mcpId], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        DesktopJobDetail? saved = null;
        var vm = new JobsPageViewModel(
            (_, _) => Task.FromResult(definition),
            (candidate, _) => Task.FromResult(saved = candidate),
            (_, _, _) => Task.FromResult(new DesktopActionResponse(
                "Succeeded", "Completed", Guid.NewGuid(),
                [new DesktopRunShard(0, 0, "Succeeded", "{\"ok\":true}", "native log")])));
        vm.Update(Snapshot(project, job));

        vm.SelectedJob = job;
        await WaitUntilAsync(() => vm.HasDefinition);
        vm.Name = "Deploy native";
        vm.SelectedFile = vm.Files.Single(file => file.Path == "main.py");
        vm.SelectedFile.Content = "print('native')";
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal("Deploy native", saved.Name);
        Assert.Equal("print('native')", saved.MapFiles.Single(file => file.Path == "main.py").Content);
        Assert.Equal("print('reduce')", saved.ReduceFiles.Single().Content);
        Assert.Equal("HtmlReport", Assert.Single(saved.PostJobActions));
        Assert.Equal(mcpId, Assert.Single(saved.McpConnectionIds));
        Assert.Equal("target", Assert.Single(saved.Parameters).Name);

        await vm.RunCommand.ExecuteAsync(null);
        var output = Assert.Single(vm.RunResults);
        Assert.Contains("Succeeded", output.Header);
        Assert.Equal("{\"ok\":true}", output.Output);
    }

    [Fact]
    public void New_job_opens_a_native_editable_source_file()
    {
        var project = new CoreProject(Guid.NewGuid(), "Production", "/srv/production", "Ready", true);
        var vm = new JobsPageViewModel(
            (_, _) => throw new InvalidOperationException(),
            (candidate, _) => Task.FromResult(candidate),
            (_, _, _) => throw new InvalidOperationException());
        vm.Update(Snapshot(project));

        vm.NewJobCommand.Execute(null);

        Assert.True(vm.IsNew);
        Assert.True(vm.IsCodeJob);
        Assert.Equal("main.py", Assert.Single(vm.Files).Path);
        Assert.Contains("json.loads", vm.SelectedFile!.Content);
    }

    private static WorkspaceSnapshot Snapshot(CoreProject project, params CoreJob[] jobs) =>
        new([project], jobs, [], [], [], [], [], [], [], [], [], [], [], []);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(5);
        Assert.True(condition());
    }
}
