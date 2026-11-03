using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Operations;
using PlaceContext.Application;
using PlaceContext.Host.Components.Pages;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Host.Tests;

public sealed class ObservabilityLogRenderingTests
{
    [Fact]
    public async Task Large_json_is_previewed_in_html_but_remains_searchable_and_inspectable()
    {
        var message = "{\"payload\":\"" + new string('x', 1_000_000) + "tail-marker\"}";
        var run = new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Succeeded",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [new(0, 0, "Succeeded", null, message, [])], null,
            new("code", "rust", null, null, null, 1, 1, false));
        var report = new RunReportView(run.JobId, "Large report", "Ossen", run);
        var service = new PlaceContextService(new Reports(report), null!, null!, null!);
        var vm = new ObservabilityViewModel(service, new PortalUiState(), new Navigation(),
            new OperationCenter(null!, default), null!, new InMemoryWorkloadOutputBuffer(), null!)
        {
            LogQueryText = "where message contains 'tail-marker' | fields message"
        };
        var services = new ServiceCollection().AddLogging().AddSingleton(vm).AddTransient(_ => new RunOutputViewModel(null!));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await vm.RestoreTabAsync("logs");
            var rendered = await renderer.RenderComponentAsync<LogsPage>();
            var html = rendered.ToHtmlString();
            Assert.Contains(new string('x', 100), html);
            Assert.DoesNotContain(new string('x', 501), html);
            Assert.True(html.Length < 30_000, $"Rendered {html.Length} characters for one row");
            var entry = Assert.Single(vm.LogResults!);
            Assert.Equal(message, entry.Event.Message);
            vm.ToggleLogEvent(entry);
            Assert.Equal(message, vm.SelectedLog!.Event.Message);
            Assert.Equal(message, vm.SelectedLogFields!.Cell("message"));
        });
    }

    private sealed class Reports(RunReportView report) : IDispatcher
    {
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default) => Task.FromResult((T)(object)(query switch
        {
            ListRecentRunReportsQuery => (IReadOnlyList<RunReportView>)[report],
            ListRecentChainRunsQuery => (IReadOnlyList<ChainRunReportView>)[],
            GetJobRunQuery => report.Run,
            _ => throw new InvalidOperationException(query.GetType().Name),
        }));
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class LogsPage : Observability
    {
        protected override Task OnParametersSetAsync() => Task.CompletedTask;
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://localhost/", "http://localhost/observability?tab=logs");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }
}
