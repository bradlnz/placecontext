using PlaceContext.Host.Components.ViewModels;

namespace PlaceContext.Host.Tests;

public sealed class JobEditorDefaultsTests
{
    [Fact]
    public void Execution_results_are_collapsed_by_default()
    {
        var viewModel = new JobEditorViewModel(null!, null!, null!, null!);

        Assert.False(viewModel.PanelOpen);
    }
}
