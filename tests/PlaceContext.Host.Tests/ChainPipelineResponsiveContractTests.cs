namespace PlaceContext.Host.Tests;

public sealed class ChainPipelineResponsiveContractTests
{
    [Fact]
    public void Running_steps_wrap_and_remain_contained_on_mobile()
    {
        var pipelineStyles = ReadHostSource("Components/Shared/ChainPipeline.razor.css");
        var chainsStyles = ReadHostSource("Components/Pages/JobChains.razor.css");
        const string breakpoint = "@media (max-width: 950px)";

        var responsivePipeline = pipelineStyles.Split(breakpoint, 2)[1];
        var responsiveChains = chainsStyles.Split(breakpoint, 2)[1];

        Assert.Contains("flex-wrap: wrap", responsivePipeline);
        Assert.Contains("overflow-x: hidden", responsivePipeline);
        Assert.Contains(".stage-col", responsivePipeline);
        Assert.Contains("min-width: 0", responsivePipeline);
        Assert.DoesNotContain("flex-wrap:nowrap", responsivePipeline);
        Assert.Contains(".running-steps-panel", responsiveChains);
        Assert.Contains("max-width: 100%", responsiveChains);
        Assert.Contains("overflow: hidden", responsiveChains);
    }

    private static string ReadHostSource(string relativePath)
    {
        var hostRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/PlaceContext.Host"));

        return File.ReadAllText(Path.Combine(hostRoot, relativePath));
    }
}
