using Microsoft.Extensions.Options;
using PlaceContext.Application.Workload.VirtualMachines;
using PlaceContext.Infrastructure.Workload;
using Xunit.Abstractions;

namespace PlaceContext.Infrastructure.Tests;

public sealed class CliAgentIntegrationTests(ITestOutputHelper output)
{
    private sealed class CodexDesktopFactAttribute : FactAttribute
    {
        public CodexDesktopFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_CODEX_MODEL")))
            {
                Skip = "Opt in with PLACECONTEXT_TEST_QEMU_IMAGE and PLACECONTEXT_TEST_CODEX_MODEL; uses the local Codex login and remote inference.";
            }
        }
    }

    private sealed class VerifiedAgent(IVirtualMachineTaskAgent agent, string title) : IVirtualMachineTaskAgent
    {
        public string? ConfigurationError => agent.ConfigurationError;
        public string Model => agent.Model;
        public bool BrowserVerified
        {
            get; private set;
        }
        public async Task<VirtualMachineEnvironmentResult> RunAsync(string prompt, string? input,
            Func<string, int, CancellationToken, Task<VirtualMachineEnvironmentResult>> executeGuest, CancellationToken ct,
            Func<VirtualMachineReplayCapture, CancellationToken, Task>? recordFrame = null, VirtualMachineAgentInteraction? interaction = null, Func<CancellationToken, Task<byte[]>>? readLiveFrame = null)
        {
            var result = await agent.RunAsync(prompt, input, executeGuest, ct, recordFrame, interaction, readLiveFrame);
            var check = await executeGuest("test \"$(id -u)\" != 0 && xwininfo -root -tree | grep -F -- '" + title + "'", 8192, ct);
            BrowserVerified = check.ExitCode == 0;
            return result;
        }
    }

    [CodexDesktopFact]
    public async Task Signed_in_local_Codex_opens_and_visually_checks_a_real_Qemu_browser()
    {
        var model = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_CODEX_MODEL")!;
        var config = Options.Create(new VirtualMachineEnvironmentOptions
        {
            Enabled = true,
            UseKvm = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_KVM") == "true",
            Images = new() { ["browser"] = Environment.GetEnvironmentVariable("PLACECONTEXT_TEST_QEMU_IMAGE")! },
            Agent = new() { Provider = "codex", Model = model, MaxSteps = 12 },
        });
        using var unusedHttp = new VirtualMachinePromptAgentTests.ModelApi();
        var title = "Codex QEMU " + Guid.NewGuid().ToString("N")[..10];
        var agent = new VerifiedAgent(new VirtualMachinePromptAgent(unusedHttp, config), title);
        var runner = new VirtualMachineEnvironmentRunner(config, agent);
        var directories = Directory.GetDirectories(Path.GetTempPath(), "pcq-*").Order().ToArray();
        var result = await runner.RunAsync(new("browser", $"Create a local HTML page whose title and visible heading are '{title}'. Open it in Chromium with its sandbox enabled. Inspect the Live preview using live_desktop and verify the title and heading. Leave the browser open, then finish with a summary including the exact title. Guest networking is disabled; this is an offline desktop test.",
            memoryMb: 2048, cpus: 2, timeoutSeconds: 300), "{}", default);
        output.WriteLine($"Model: {model}\nExit code: {result.ExitCode}\nSteps: {result.AgentSteps}\nSummary: {result.Stdout}\nError: {result.Stderr}");
        foreach (var activity in result.Activity ?? [])
        {
            output.WriteLine($"{activity.Tool}: {activity.Description} (exit {activity.ExitCode})");
        }
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.True(agent.BrowserVerified, "Independent guest check could not find the requested Chromium window.");
        Assert.Contains(title, result.Stdout);
        Assert.Contains(result.Activity!, activity => activity.Tool == "guest_shell" && activity.ExitCode == 0);
        Assert.Contains(result.Activity!, activity => activity.Tool == "live_desktop");
        Assert.Empty(unusedHttp.Requests);
        Assert.Equal(directories, Directory.GetDirectories(Path.GetTempPath(), "pcq-*").Order().ToArray());
    }
}
