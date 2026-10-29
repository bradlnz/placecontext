using Microsoft.Extensions.Options;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Workload;

namespace PlaceContext.Infrastructure.Tests;

public sealed class QemuEnvironmentRunnerTests
{
    [Fact]
    public void Image_catalog_reports_missing_profiles_without_exposing_host_paths()
    {
        var path = Path.GetTempFileName();
        try
        {
            var runner = new QemuEnvironmentRunner(Options.Create(new QemuEnvironmentOptions
                { Enabled = true, Images = new() { ["browser"] = path, ["missing"] = path + ".absent", ["relative"] = "relative.qcow2" } }));
            var catalog = runner.Get();
            Assert.True(catalog.Enabled);
            Assert.True(catalog.Images.Single(image => image.Name == "browser").Available);
            Assert.False(catalog.Images.Single(image => image.Name == "missing").Available);
            Assert.False(catalog.Images.Single(image => image.Name == "relative").Available);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void Launch_uses_disposable_disk_and_private_agent_socket_without_host_shell()
    {
        var action = new QemuChainAction("linux", "echo '$HOME; touch /host-file'");
        var start = QemuEnvironmentRunner.BuildStartInfo(new(), action, "/images/a,b.qcow2", "/tmp/agent.sock");
        Assert.False(start.UseShellExecute);
        Assert.Contains("-snapshot", start.ArgumentList);
        Assert.Contains("virtio-vga", start.ArgumentList);
        Assert.Contains("virtio-rng-pci,max-bytes=1024,period=1000", start.ArgumentList);
        Assert.Contains("max", start.ArgumentList);
        Assert.Contains("file=/images/a,,b.qcow2,format=qcow2,if=virtio", start.ArgumentList);
        Assert.Contains("none", start.ArgumentList);
        Assert.DoesNotContain(action.Command, start.ArgumentList);
        Assert.DoesNotContain(start.ArgumentList, arg => arg.Contains("hostfwd") || arg.Contains("virtfs"));
        Assert.Equal("none", start.ArgumentList[^1]);
        var networked = QemuEnvironmentRunner.BuildStartInfo(new() { UseKvm = true },
            new("linux", "cat", allowNetwork: true), "/images/base.qcow2", "/tmp/agent.sock");
        Assert.Contains("kvm", networked.ArgumentList);
        Assert.Contains("host", networked.ArgumentList);
        Assert.Equal("user,model=virtio-net-pci", networked.ArgumentList[^1]);
    }

    [Fact]
    public void Guest_instructions_are_separate_arguments_after_dropping_root_and_waiting_for_the_desktop()
    {
        const string command = "printf '%s' \"$HOME\"; id";
        var execution = QemuEnvironmentRunner.BuildGuestExecution(command, "{\"hello\":true}");
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
        var runner = new QemuEnvironmentRunner(Options.Create(new QemuEnvironmentOptions()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(new("linux", "cat"), null, default));
        runner = new QemuEnvironmentRunner(Options.Create(new QemuEnvironmentOptions { Enabled = true }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(new("linux", "cat"), null, default));
    }
}
