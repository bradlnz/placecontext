using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class JevQemuIntegrationTests
{
    private sealed class DesktopFactAttribute : FactAttribute
    {
        public DesktopFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE")))
                Skip = "Set PLACECONTEXT_TEST_QEMU_IMAGE to test the Rust Jev loop in a real VM.";
        }
    }
    private sealed class FixtureAgent(IQemuTaskAgent agent) : IQemuTaskAgent
    {
        public string? ConfigurationError => agent.ConfigurationError;
        public string Model => agent.Model;
        public async Task<QemuEnvironmentResult> RunAsync(string prompt, string? input,
            Func<string, int, CancellationToken, Task<QemuEnvironmentResult>> executeGuest, CancellationToken ct,
            Func<QemuReplayCapture, CancellationToken, Task>? recordFrame = null, QemuAgentInteraction? interaction = null, Func<CancellationToken, Task<byte[]>>? readLiveFrame = null)
        {
            const string html = """
                <!doctype html><title>Order Desk fixture</title>
                <form>
                <label style="position:absolute;left:20px;top:10px">Customer email</label><input name="customer" type="email" required style="position:absolute;left:20px;top:40px;width:300px;height:40px;background:#ffeedd">
                <label style="position:absolute;left:20px;top:100px">Quantity of Team kits ($79 each)</label><input name="quantity" type="number" min="1" max="50" value="1" style="position:absolute;left:20px;top:130px;width:300px;height:40px;background:#ddeeff">
                <button style="position:absolute;left:20px;top:200px;width:300px;height:40px;background:#eeddff">Place order</button></form>
                <div id="result" style="position:absolute;top:270px"></div><script>
                document.querySelector('form').onsubmit = event => {
                    event.preventDefault(); const data = new FormData(event.target);
                    const subtotal = 79 * Number(data.get('quantity'));
                    const shipping = subtotal >= 100 ? 0 : 9;
                    document.querySelector('#result').textContent = `Order ready for ${data.get('customer')}. Total AUD ${subtotal + shipping}. Shipping AUD ${shipping}.`;
                    fetch('/verified-order?email=' + encodeURIComponent(data.get('customer')) + '&total=' + (subtotal + shipping));
                };
                </script>
                """;
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
            var setup = await executeGuest($"mkdir -p /tmp/order-fixture\nprintf '%s' '{encoded}' | base64 -d > /tmp/order-fixture/orders.html\npython3 -m http.server 8080 --bind 127.0.0.1 --directory /tmp/order-fixture >/tmp/order-fixture/http.log 2>&1 < /dev/null &", 8192, ct);
            Assert.Equal(0, setup.ExitCode);
            var result = await agent.RunAsync(prompt, input, executeGuest, ct, recordFrame, interaction, readLiveFrame);
            var log = await executeGuest("cat /tmp/order-fixture/http.log",8192,ct);
            Assert.Contains("/verified-order?email=buyer%40example.test&total=158",log.Stdout);
            return result;
        }
    }

    [DesktopFact]
    public async Task Rust_loop_fills_an_order_and_verifies_the_total_in_an_unchanged_Qemu_image()
    {
        var visionTurns = 0;
        using var api = new JevTestApi(async (request, ct) =>
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            if (request.RequestUri!.Host == "typing.test")
            {
                Assert.Contains("data:image/png;base64,",body.ToJsonString());
                var action = new[] { "type", "type", "click", "done" }[visionTurns];
                var imageUrl = body["messages"]![1]!["content"]![1]!["image_url"]!["url"]!.GetValue<string>();
                var png = Convert.FromBase64String(imageUrl["data:image/png;base64,".Length..]);
                var color = new (byte R, byte G, byte B)[] { (255,238,221), (221,238,255), (238,221,255) };
                var point = visionTurns < 3 ? QemuDesktopFeedTests.FindColor(png, color[visionTurns].R, color[visionTurns].G, color[visionTurns].B) : (X:0,Y:0);
                var text = visionTurns == 0 ? "buyer@example.test" : "2";
                visionTurns++;
                var proposal = JsonSerializer.Serialize(new { action, x=point.X,y=point.Y,text,description=action,
                    observation="Total AUD 158. Shipping AUD 0." });
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = proposal } } } })) };
            }
            Assert.Equal("true",Assert.Single(request.Headers.GetValues("TypeSafe-Danger-Zone-Images")));
            Assert.NotNull(body["assets"]!["screen-image"]!["data_base64"]);
            var criteria=body["questions"]!["action"]!["criteria"]!.AsObject();
            var choice=visionTurns==4 ? "done" : "act";
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                model="jev-scripted",answers=new {
                    action=new {type="choice",choice,confidence=0.74,probabilities=criteria.ToDictionary(x=>x.Key,x=>x.Key==choice?1.0:0.0)},
                    goal_done=new {type="noul",noul=choice=="done"?.99:0.0},stuck=new {type="noul",noul=0.0}
                }
            })) };
        });
        var config = Options.Create(new QemuEnvironmentOptions
        {
            Enabled = true, UseKvm = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_KVM") == "true",
            Images = new() { ["browser"] = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE")! },
            Agent = new() { Provider = "chat-completions", Model = "typing", Endpoint = "http://typing.test/completions", MaxSteps = 8 },
        });
        var agent = new JevQemuTaskAgent(api, config, Options.Create(new TypeSafeOptions { ApiKey = "host-test-key" }));
        var runner = new QemuEnvironmentRunner(config, new FixtureAgent(agent));
        var frames = new List<QemuReplayCapture>();
        var result = await runner.RunAsync(new("browser", "Start at http://127.0.0.1:8080/orders.html. Order two Team kits for buyer@example.test. Verify the order is ready with total AUD 158 and free shipping.",
            memoryMb: 2048, cpus: 2, timeoutSeconds: 180), null, default,
            (frame, _) => { frames.Add(frame); return Task.CompletedTask; });
        Assert.True(result.ExitCode == 0, result.Stderr + "\n" + result.Stdout);
        Assert.Contains("Total AUD 158. Shipping AUD 0.", result.Stdout);
        Assert.Equal(4, result.AgentSteps);
        Assert.Equal(4, visionTurns);
        Assert.Contains(frames, frame => frame.Frame.Click && frame.Frame.PointerX is not null);
        Assert.Contains(frames, frame => frame.Frame.HasImage);
    }
}
