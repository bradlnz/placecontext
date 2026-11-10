namespace PlaceContext.Infrastructure.Workload;

public sealed class VirtualMachineEnvironmentOptions
{
    public bool Enabled
    {
        get; set;
    } = true;
    public string Executable { get; set; } = "qemu-system-x86_64";
    public bool UseKvm
    {
        get; set;
    }
    public string Executor { get; set; } = "jev";
    // Operator-owned, prepared Linux desktop qcow2 images with qemu-guest-agent and a non-root workflow user.
    public Dictionary<string, string> Images { get; set; } = new();
    public VirtualMachineAgentOptions Agent { get; set; } = new();
}
