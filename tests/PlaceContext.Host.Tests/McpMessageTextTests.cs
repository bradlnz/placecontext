using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlaceContext.Host.Components.Shared;

namespace PlaceContext.Host.Tests;

public sealed class McpMessageTextTests
{
    [Fact]
    public async Task Mentions_are_bold_without_syntax_quotes_and_message_text_is_encoded()
    {
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<McpMessageText>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Text"] = "Ask @\"Commerce MCP\" and @Warehouse. <script>alert(1)</script> user@example.test" }))).ToHtmlString());
        Assert.Contains("<strong>@Commerce MCP</strong>", html);
        Assert.Contains("<strong>@Warehouse</strong>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("user@example.test", html);
        Assert.DoesNotContain("&quot;Commerce", html);
    }
}
