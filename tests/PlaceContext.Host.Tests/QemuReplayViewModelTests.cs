using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class QemuReplayViewModelTests
{
    private static ChainRunView Run(string status = "Succeeded") => new(Guid.NewGuid(), Guid.NewGuid(), "Desktop", status,
        [new(0, 0, 0, Guid.Empty, "QEMU environment", null, status, DateTimeOffset.UtcNow, null, QemuChainAction.ActionType),
         new(1, 1, 0, Guid.Empty, "Second desktop", null, "Succeeded", DateTimeOffset.UtcNow, null, QemuChainAction.ActionType)],
        null, DateTimeOffset.UtcNow, null) { ProjectId = Guid.NewGuid() };
    private static async Task AddAsync(FakeQemuReplayStore store, Guid runId, int step, int sequence) =>
        await store.AppendAsync(runId, step, new(new(sequence, sequence * 1500, DateTimeOffset.UtcNow, $"Action {sequence}", 0, true), [1]), default);

    [Fact]
    public async Task Replay_seeks_plays_and_switches_steps_and_runs_without_leaking_old_frames()
    {
        var store = new FakeQemuReplayStore();
        var run = Run();
        for (var i = 0; i < 3; i++) await AddAsync(store, run.Id, 0, i);
        await AddAsync(store, run.Id, 1, 0);
        using var vm = new QemuReplayViewModel(store);
        await vm.SetRunAsync(run);
        Assert.Equal(3, vm.Frames.Count);
        Assert.Contains($"/projects/{run.ProjectId}/workflow-runs/{run.Id}/qemu/0/frames/0", vm.ImageUrl);
        vm.Seek("99"); Assert.Equal(2, vm.Position);
        var play = vm.TogglePlaybackAsync();
        Assert.True(vm.Playing); Assert.Equal(0, vm.Position);
        await Task.Delay(1150);
        Assert.Equal(1, vm.Position);
        vm.Select(0); await play;
        Assert.False(vm.Playing);
        await vm.SelectStepAsync("1");
        Assert.Single(vm.Frames); Assert.Contains("/qemu/1/", vm.ImageUrl);
        await vm.SetRunAsync(Run());
        Assert.Empty(vm.Frames); Assert.Null(vm.ImageUrl);
    }

    [Fact]
    public async Task Active_and_cancelled_runs_keep_recorded_frames_and_refresh_the_final_capture()
    {
        var store = new FakeQemuReplayStore();
        var run = Run("Running");
        using var vm = new QemuReplayViewModel(store);
        await vm.SetRunAsync(run);
        Assert.True(vm.Recording); Assert.Empty(vm.Frames);
        await AddAsync(store, run.Id, 0, 0);
        await vm.SetRunAsync(run);
        Assert.Single(vm.Frames);
        await AddAsync(store, run.Id, 0, 1);
        await vm.SetRunAsync(run with { Status = "Cancelled", Steps = run.Steps.Select(step => step with { Status = "Cancelled" }).ToArray() });
        Assert.False(vm.Recording); Assert.Equal(2, vm.Frames.Count);
        vm.MarkImageFailed(); Assert.True(vm.ImageFailed);
        vm.Select(1); Assert.False(vm.ImageFailed);
    }
}
