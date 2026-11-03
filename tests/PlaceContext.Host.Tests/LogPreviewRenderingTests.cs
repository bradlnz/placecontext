using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Dtos;
using PlaceContext.Host.Components.Shared;
using PlaceContext.Host.Components.ViewModels;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Host.Tests;

public sealed class LogPreviewRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Log_tables_bound_rows_and_messages_without_losing_searchable_text(bool container)
    {
        var text = string.Join('\n', Enumerable.Range(1, 600).Select(i => $"row-{i:D4} " + new string('x', 5000))) + "tail-marker";
        var jobVm = new JobRunLogsViewModel(new InMemoryWorkloadOutputBuffer());
        var containerVm = new ContainerLogsViewModel();
        var services = new ServiceCollection().AddLogging().AddSingleton(jobVm).AddSingleton(containerVm)
            .AddTransient(_ => new ArtifactDownloadViewModel(null!));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var run = new JobRunDetailView(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Succeeded",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [new(0, 0, "Succeeded", null, text, [])], null,
                new("code", "python", null, null, null, 1, 1, false));
            var rendered = container
                ? await renderer.RenderComponentAsync<ContainerLogs>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Text"] = text }))
                : await renderer.RenderComponentAsync<JobRunLogs>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Run"] = run }));
            var html = rendered.ToHtmlString();
            Assert.Equal(101, Regex.Matches(html, "<tr(?:>| )").Count);
            Assert.DoesNotContain("row-0001", html);
            Assert.Contains("row-0600", html);
            Assert.DoesNotContain(new string('x', 2001), html);
            Assert.DoesNotContain("data:", html);
            Assert.True(html.Length < 300_000, $"Rendered {html.Length} characters");
            Assert.Contains("Download logs", html);
            jobVm.Search = containerVm.Search = "tail-marker";
            var match = Assert.Single(container ? containerVm.Filtered : jobVm.Filtered);
            Assert.EndsWith("tail-marker", match.Message);
            if (!container)
            {
                Assert.EndsWith(text, jobVm.FullLog);
            }
        });
    }

    [Fact]
    public async Task Rendering_a_download_button_does_not_read_or_embed_its_content()
    {
        var services = new ServiceCollection().AddLogging().AddTransient(_ => new ArtifactDownloadViewModel(null!));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<ArtifactDownload>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Label"] = "Download logs",
                ["GetArtifact"] = (Func<RunArtifactView>)(() => throw new InvalidOperationException("Must only read on click")),
            }));
            Assert.Contains("Download logs", rendered.ToHtmlString());
        });
    }
}
