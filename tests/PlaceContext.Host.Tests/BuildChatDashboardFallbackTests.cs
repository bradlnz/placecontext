using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Build;
using PlaceContext.Application.Features;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Persistence;

namespace PlaceContext.Host.Tests;

public sealed partial class BuildChatTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dashboard_without_a_selected_model_shows_live_job_charts_and_preserves_saved_tiles(bool noConnections)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        if (noConnections)
        {
            using var scope = fixture.Provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ModelConnections.SingleAsync()).Enabled = false;
            await db.SaveChangesAsync();
        }
        var savedChart = new DashboardChart(new ChartSpec("bar", "Saved chart", ["Running"], [new("Runs", [1])]).ToJson(), "Saved job chart") { Live = true };
        await fixture.Store.SaveSettingsAsync(fixture.ProjectId, new() { Charts = [savedChart] });
        var dispatcher = new ToolDispatcher(fixture.ProjectId);
        dispatcher.Counts["Failed"] = 2;
        dispatcher.Counts["Succeeded"] = 7;
        var vm = new BuildChatViewModel(fixture.Store, fixture.Connections, null!, new(), new NoJs(),
            new PlaceContextService(dispatcher, null!, null!, null!), fixture.Permissions);
        await vm.LoadAsync(fixture.ProjectId);
        vm.Conversation.Model = "";
        await vm.RefreshDashboardChartsAsync();
        Assert.False(vm.HasSelectedModel);
        Assert.False(vm.CanStartChat);
        Assert.Equal(2, vm.DashboardTileKeys.Count());
        Assert.Equal(savedChart, Assert.Single(vm.Tiles.Charts));
        var charts = vm.DisplayedTiles.Charts;
        Assert.Equal(new double[] { 3, 0, 2, 7 }, vm.DashboardChartResults[charts[0].Id].Spec!.Series[0].Values);
        Assert.Equal(new[] { "Failed (24h)", "Succeeded (24h)" }, vm.DashboardChartResults[charts[1].Id].Spec!.Labels);
        Assert.Equal(new double[] { 2, 7 }, vm.DashboardChartResults[charts[1].Id].Spec!.Series[0].Values);
        Assert.Equal(noConnections ? "/settings/models" : $"/project/{fixture.ProjectId}/dashboard/settings/agent", vm.AddModelHref);

        await using var provider = PageServices(vm).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlaceContext.Host.Components.Pages.BuildChat>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["ProjectId"] = fixture.ProjectId }))).ToHtmlString());
        Assert.Contains("Add a model", html);
        Assert.Contains("Job run status", html);
        Assert.Contains("Job outcomes (24 hours)", html);
        Assert.DoesNotContain("dashboard-drag-handle", html);
        Assert.DoesNotContain("dashboard-resize", html);

        dispatcher.Counts.Clear();
        await vm.RefreshDashboardChartsAsync();
        Assert.All(charts, chart => Assert.All(vm.DashboardChartResults[chart.Id].Spec!.Series[0].Values, value => Assert.Equal(0, value)));
        fixture.Permissions.Denied = Permission.JobsView;
        await vm.RefreshDashboardChartsAsync();
        Assert.All(charts, chart => { Assert.Null(vm.DashboardChartResults[chart.Id].Spec); Assert.NotNull(vm.DashboardChartResults[chart.Id].Error); });
        fixture.Permissions.Denied = null;
        Assert.Equal(savedChart, Assert.Single((await fixture.Store.GetSettingsAsync(fixture.ProjectId)).Charts));
        Assert.Empty(vm.Conversation.Messages);
        Assert.Equal(0, dispatcher.Commands);
        if (!noConnections)
        {
            vm.Conversation.Model = "test";
            await vm.RefreshDashboardChartsAsync();
            Assert.True(vm.HasSelectedModel);
            Assert.Same(vm.Tiles, vm.DisplayedTiles);
            Assert.NotNull(vm.DashboardChartResults[savedChart.Id].Spec);
        }
        await vm.DisposePageAsync();
    }
}
