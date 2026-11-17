using PlaceContext.Application.Build;
using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class ModelsSettingsViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_empty_discovery_keeps_configured_models(bool failure)
    {
        var connections = new Connections { Fail = failure };
        var vm = new ModelsSettingsViewModel(connections, new PortalUiState());
        vm.Edit(connections.Current);
        await vm.DiscoverAsync();
        Assert.NotNull(vm.Error);
        Assert.Null(vm.Notice);
        Assert.Equal("existing-model", vm.Models);
        Assert.Equal(["existing-model"], connections.Current.Models);
        Assert.True(vm.Editing);
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task Successful_discovery_is_reviewable_before_replacing_available_models()
    {
        var connections = new Connections { Found = ["discovered-model"] };
        var vm = new ModelsSettingsViewModel(connections, new PortalUiState());
        vm.Edit(connections.Current);
        await vm.DiscoverAsync();
        Assert.Null(vm.Error);
        Assert.Equal("discovered-model", vm.Models);
        Assert.Equal(["existing-model"], connections.Current.Models);
        await vm.SaveAsync();
        Assert.Equal(["discovered-model"], connections.Current.Models);
    }

    private sealed class Connections : IModelConnections
    {
        public bool Fail;
        public IReadOnlyList<string> Found = [];
        public ModelConnection Current = new(Guid.NewGuid(), "Codex", "codex", "cli://codex", ["existing-model"], true, false);
        public Task<IReadOnlyList<ModelConnection>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ModelConnection>>([Current]);
        public Task SaveAsync(SaveModelConnection connection, CancellationToken ct = default)
        {
            Current = Current with { Models = connection.Models };
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<string>> DiscoverAsync(Guid id, CancellationToken ct = default) =>
            Fail ? throw new InvalidOperationException("CLI not installed in the app environment") : Task.FromResult(Found);
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
