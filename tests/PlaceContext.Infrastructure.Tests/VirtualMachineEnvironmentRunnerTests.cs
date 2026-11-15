using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class VirtualMachineEnvironmentRunnerTests
{
    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(4102)]
    public async Task Complete_guest_reply_does_not_wait_for_more_data_on_an_open_connection(int size)
    {
        var pipe = new System.IO.Pipelines.Pipe();
        using var stream = pipe.Reader.AsStream();
        using var output = new MemoryStream();
        using var writer = new StreamWriter(output) { AutoFlush = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        const string prefix = "{\"return\":{\"value\":\"";
        const string suffix = "\"}}\n";
        var value = new string('x', size - prefix.Length - suffix.Length - 4) + "🙂";
        await pipe.Writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes(prefix + value + suffix));
        try
        {
            var result = await VirtualMachineEnvironmentRunner.CallAsync(stream, writer, "guest-ping", new { }, timeout.Token);
            Assert.Equal(value, result.GetProperty("value").GetString());
        }
        finally
        {
            await pipe.Writer.CompleteAsync();
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Configuration_enables_qemu_by_default_and_respects_explicit_overrides(string? enabled, bool expected)
    {
        var values = new Dictionary<string, string?>();
        if (enabled is not null)
        {
            values["PlaceContext:Qemu:Enabled"] = enabled;
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new VirtualMachineEnvironmentOptions();
        configuration.GetSection("PlaceContext:Qemu").Bind(options);
        var catalog = new VirtualMachineEnvironmentRunner(Options.Create(options)).Get();

        Assert.Equal(expected, catalog.Enabled);
        Assert.Empty(catalog.Images);
        Assert.False(catalog.AgentReady);
        Assert.False(options.UseKvm);
        Assert.Equal("jev", options.Executor);
    }

    [Fact]
    public void Image_catalog_reports_missing_profiles_without_exposing_host_paths()
    {
        var path = Path.GetTempFileName();
        try
        {
            var runner = new VirtualMachineEnvironmentRunner(Options.Create(new VirtualMachineEnvironmentOptions
            {
                Images = new() { ["browser"] = path, ["missing"] = path + ".absent", ["relative"] = "relative.qcow2" }
            }));
            var catalog = runner.Get();
            Assert.True(catalog.Enabled);
            Assert.True(catalog.Images.Single(image => image.Name == "browser").Available);
            Assert.False(catalog.Images.Single(image => image.Name == "missing").Available);
            Assert.False(catalog.Images.Single(image => image.Name == "relative").Available);
        }
        finally
        {
            File.Delete(path);
        }
    }
    [Fact]
    public void Launch_uses_disposable_disk_and_private_agent_socket_without_host_shell()
    {
        var action = new VirtualMachineChainAction("linux", "echo '$HOME; touch /host-file'");
        var start = VirtualMachineEnvironmentRunner.BuildStartInfo(new(), action, "/images/a,b.qcow2", "/tmp/agent.sock");
        Assert.False(start.UseShellExecute);
        Assert.Contains("-snapshot", start.ArgumentList);
        Assert.Contains("virtio-vga", start.ArgumentList);
        Assert.Contains("virtio-rng-pci,max-bytes=1024,period=1000", start.ArgumentList);
        Assert.Contains("max", start.ArgumentList);
        Assert.Contains("tcg,thread=multi", start.ArgumentList);
        Assert.Contains("file=/images/a,,b.qcow2,format=qcow2,if=virtio", start.ArgumentList);
        Assert.Contains("none", start.ArgumentList);
        Assert.DoesNotContain(action.Command, start.ArgumentList);
        Assert.DoesNotContain(start.ArgumentList, arg => arg.Contains("hostfwd") || arg.Contains("virtfs"));
        Assert.Equal("none", start.ArgumentList[^1]);
        var networked = VirtualMachineEnvironmentRunner.BuildStartInfo(new()
        {
            UseKvm = true
        },
            new("linux", "cat", allowNetwork: true), "/images/base.qcow2", "/tmp/agent.sock");
        Assert.Contains("kvm", networked.ArgumentList);
        Assert.Contains("host", networked.ArgumentList);
        Assert.Equal("user,model=virtio-net-pci", networked.ArgumentList[^1]);
        var live = VirtualMachineEnvironmentRunner.BuildStartInfo(new(), action, "/images/base.qcow2", "/tmp/agent.sock", "/tmp/desktop.sock");
        Assert.Equal("unix:/tmp/desktop.sock,share=force-shared", live.ArgumentList[^1]);
    }

    [Fact]
    public void Guest_instructions_are_separate_arguments_after_dropping_root_and_waiting_for_the_desktop()
    {
        const string command = "printf '%s' \"$HOME\"; id";
        var execution = VirtualMachineEnvironmentRunner.BuildGuestExecution(command, "{\"hello\":true}");
        Assert.Equal("/usr/bin/setpriv", execution["path"]);
        var args = Assert.IsType<string[]>(execution["arg"]);
        Assert.Contains("--reuid=workflow", args);
        Assert.Contains("--regid=workflow", args);
        Assert.Contains("--clear-groups", args);
        Assert.Contains("--no-new-privs", args);
        Assert.Contains("--bounding-set=-all", args);
        Assert.Contains("--inh-caps=-all", args);
        Assert.Contains("--ambient-caps=-all", args);
        Assert.Contains("DISPLAY=:0", args);
        Assert.Contains("XAUTHORITY=/home/workflow/.Xauthority", args);
        Assert.Equal(command, args[^1]);
        Assert.Contains("id -u", args[^3]);
        Assert.Contains("_NET_SUPPORTING_WM_CHECK", args[^3]);
        Assert.DoesNotContain(command, args[^3]);
        Assert.Equal("{\"hello\":true}", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)execution["input-data"])));
    }

    [Fact]
    public async Task Disabled_and_unconfigured_images_fail_before_starting_a_process()
    {
        var runner = new VirtualMachineEnvironmentRunner(Options.Create(new VirtualMachineEnvironmentOptions { Enabled = false }));
        Assert.False(runner.Get().Enabled);
        var disabled = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(new("linux", "cat"), null, default));
        Assert.Contains("not enabled", disabled.Message);

        runner = new VirtualMachineEnvironmentRunner(Options.Create(new VirtualMachineEnvironmentOptions()));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(new("linux", "cat"), null, default));
        Assert.Contains("image profile 'linux' is not available", missing.Message);
    }

    [Fact]
    public async Task Default_enabled_runner_still_requires_an_agent_before_starting_a_process()
    {
        var path = Path.GetTempFileName();
        try
        {
            var options = new VirtualMachineEnvironmentOptions { Images = new() { ["linux"] = path } };
            var runner = new VirtualMachineEnvironmentRunner(Options.Create(options));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(new("linux", "cat"), null, default));
            Assert.Contains("Configure a QEMU task agent", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
