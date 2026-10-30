using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class PersonalDashboardsViewModelTests
{
    [Fact]
    public async Task Custom_layout_survives_save_and_analysis_is_invalidated_by_changes()
    {
        var service = new DashboardUiStub(); var js = new DashboardTestJs();
        using var vm = new PersonalDashboardsViewModel(service, new PortalUiState(), js);
        await vm.LoadAsync(Guid.NewGuid()); vm.New(); vm.Name = "My sales";
        foreach (var chart in service.Charts) vm.Toggle(chart.Id);
        vm.Move(service.Charts[1].Id, -1); vm.Question = "Is A greater than zero?";
        await vm.SaveAsync(); Assert.False(vm.Editing); Assert.Equal("My sales", vm.Selected!.Name);
        Assert.Equal(service.Charts[1].Id, vm.Selected.ChartIds[0]);
        await vm.RenderAsync(); await vm.RenderAsync(); Assert.Equal(1, js.Renders);
        await vm.AnalyzeAsync(); Assert.True(vm.AnalysisCurrent);
        vm.Question = "A different question?"; Assert.False(vm.AnalysisCurrent);
        await vm.AnalyzeAsync(); Assert.True(vm.AnalysisCurrent);
        await vm.RefreshAsync(); Assert.Null(vm.Analysis);
        await vm.RenderAsync(); Assert.Equal(2, js.Renders);
        await vm.DeleteAsync(); Assert.Single(service.Saved);
        vm.ConfirmDelete = true; await vm.DeleteAsync(); Assert.Empty(service.Saved);
    }
}
