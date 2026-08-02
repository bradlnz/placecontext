using System.Reflection;
using PlaceContext.Host.Components.Pages;

namespace PlaceContext.Host.Tests;

public sealed class JobEditorDefaultsTests
{
    [Fact]
    public void Execution_results_are_collapsed_by_default()
    {
        var panelOpen = typeof(JobEditor).GetField("_panelOpen", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(panelOpen);
        Assert.False(Assert.IsType<bool>(panelOpen.GetValue(new JobEditor())));
    }
}
