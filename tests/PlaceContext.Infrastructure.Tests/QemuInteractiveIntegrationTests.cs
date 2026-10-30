using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Workload;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;
public sealed class QemuInteractiveIntegrationTests
{
    private sealed class DesktopFactAttribute : FactAttribute
    { public DesktopFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE"))) Skip = "Set PLACECONTEXT_TEST_QEMU_IMAGE for the real browser login test."; } }
    private sealed record User(Guid UserId) : ICurrentUser { public string Role => "Owner"; public bool IsAuthenticated => true; }
    private sealed class Logins : IQemuLoginStore
    {
        public byte[]? Archive;
        public Task SaveAsync(Guid project, string name, string image, byte[] data, CancellationToken ct = default)
        { QemuProfileArchive.Validate(data); Archive = data; return Task.CompletedTask; }
        public Task<byte[]> ReadAsync(Guid project, Guid id, string image, CancellationToken ct = default) => Task.FromResult(Archive!);
        public Task<IReadOnlyList<QemuLoginProfile>> ListAsync(Guid project, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid project, Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Fixture(IQemuTaskAgent agent) : IQemuTaskAgent
    {
        public string? ConfigurationError => agent.ConfigurationError;
        public string Model => agent.Model;
        public Func<string, int, CancellationToken, Task<QemuEnvironmentResult>>? Execute;
        public async Task<QemuEnvironmentResult> RunAsync(string prompt, string? input,
            Func<string, int, CancellationToken, Task<QemuEnvironmentResult>> executeGuest, CancellationToken ct,
            Func<QemuReplayCapture, CancellationToken, Task>? recordFrame = null, QemuAgentInteraction? interaction = null, Func<CancellationToken, Task<byte[]>>? readLiveFrame = null)
        {
            Execute = executeGuest;
            const string server = """
                from http.server import BaseHTTPRequestHandler,HTTPServer
                class Handler(BaseHTTPRequestHandler):
                  def do_GET(self):
                    signed = 'session=private-test-login' in self.headers.get('Cookie','')
                    self.send_response(200)
                    self.send_header('Content-Type','text/html')
                    if self.path == '/signin': self.send_header('Set-Cookie','session=private-test-login; HttpOnly; Max-Age=3600; Path=/')
                    self.end_headers()
                    if signed:
                      open('/tmp/login-verified','w').write('restored')
                      self.wfile.write(b'<h1>Signed in</h1><p>Private test dashboard</p>')
                    else: self.wfile.write(b'<h1>Login fixture</h1><a href="/signin">Sign in</a>')
                HTTPServer(('127.0.0.1',8080),Handler).serve_forever()
                """;
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(server));
            var setup = await executeGuest($"printf '%s' '{encoded}' | base64 -d > /tmp/login-fixture.py\npython3 /tmp/login-fixture.py >/tmp/login-fixture.log 2>&1 < /dev/null &",8192,ct);
            Assert.Equal(0, setup.ExitCode);
            var result = await agent.RunAsync(prompt, input, executeGuest, ct, recordFrame, interaction, readLiveFrame);
            Assert.True(result.ExitCode == 0, result.Stderr + "\n" + result.Stdout);
            var verified = await executeGuest("test -f /tmp/login-verified && echo LOGIN_VERIFIED", 1024, ct);
            Assert.Equal("LOGIN_VERIFIED", verified.Stdout.Trim());
            return result;
        }
    }
    [DesktopFact]
    public async Task Human_login_is_paused_private_saved_and_restored_in_a_fresh_vm()
    {
        using var api = new JevTestApi(async (request, ct) =>
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
            Assert.DoesNotContain("private-test-login", body.ToJsonString());
            if (request.RequestUri!.Host == "vision.test")
            {
                var proposal = JsonSerializer.Serialize(new { action = "done", description = "Verified dashboard", observation = "Signed in" });
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = proposal } } } })) };
            }
            var criteria = body["questions"]!["action"]!["criteria"]!.AsObject();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { model = "jev-scripted", answers = new {
                action = new { type = "choice", choice = "done", confidence = .99, probabilities = criteria.ToDictionary(p=>p.Key,p=>p.Key=="done"?1.0:0.0) },
                goal_done = new { type = "noul", noul = .99 }, stuck = new { type = "noul", noul = 0.0 }
            } })) };
        });
        var options = Options.Create(new QemuEnvironmentOptions {
            Enabled = true, UseKvm = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_KVM") == "true",
            Images = new() { ["browser"] = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE")! },
            Agent = new() { Provider = "chat-completions", Endpoint = "http://vision.test/completions", Model = "test", MaxSteps = 4 }
        });
        var fixture = new Fixture(new JevQemuTaskAgent(api, options, Options.Create(new TypeSafeOptions { ApiKey = "fake-host-key" })));
        var sessions = new QemuDesktopSessions(); var tenant = new FakeCurrentTenant(Guid.NewGuid()); var user = new User(Guid.NewGuid()); var logins = new Logins();
        await using var services = new ServiceCollection().AddSingleton<IQemuLoginStore>(logins).BuildServiceProvider();
        var runner = new QemuEnvironmentRunner(options, fixture, sessions, tenant, user, services.GetRequiredService<IServiceScopeFactory>());
        var run = new QemuRunContext(Guid.NewGuid(), Guid.NewGuid(), 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var frames = new List<QemuReplayCapture>();
        const string prompt = "Start at http://127.0.0.1:8080/ and verify the signed-in dashboard.";
        var execution = runner.RunAsync(new("browser", prompt, memoryMb:2048, cpus:2, timeoutSeconds:600, pauseForLogin:true), null, timeout.Token,
            (frame,_) => { frames.Add(frame); return Task.CompletedTask; }, run);
        QemuDesktopSessions.Session? session;
        while ((session = sessions.Find(tenant.TenantId,user.UserId,run))?.Status.State != "Human")
        { if (execution.IsCompleted) { var failed = await execution; Assert.Fail(failed.Stderr); } await Task.Delay(250, timeout.Token); }
        Assert.Equal(0, api.Requests);
        Assert.Empty(frames);
        await using (var console = await session.ConnectAsync(timeout.Token))
        {
            var banner = new byte[12]; await console.ReadExactlyAsync(banner, timeout.Token);
            Assert.StartsWith("RFB ", Encoding.ASCII.GetString(banner));
        }
        // Human input is delivered to the real X11 desktop, outside the agent/model/replay path.
        var login = await fixture.Execute!("xdotool key --clearmodifiers ctrl+l; xdotool type --clearmodifiers 'http://127.0.0.1:8080/signin'; xdotool key Return; sleep 2; xdotool key ctrl+r; sleep 2", 1024, timeout.Token);
        Assert.Equal(0, login.ExitCode);
        await session.SaveAsync("Test account", timeout.Token);
        Assert.NotNull(logins.Archive);
        Assert.Empty(frames);
        Assert.Equal(0, api.Requests);
        session.Resume();
        var result = await execution;
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Null(sessions.Find(tenant.TenantId,user.UserId,run));
        Assert.NotEmpty(frames);
        var restored = await runner.RunAsync(new("browser", prompt, memoryMb:2048, cpus:2, timeoutSeconds:600, loginProfileId:Guid.NewGuid()), null, timeout.Token,
            context:run with { RunId = Guid.NewGuid() });
        Assert.True(restored.ExitCode == 0, restored.Stderr);
    }
}
