namespace PlaceContext.Infrastructure.Workload;

public sealed class QemuEnvironmentOptions
{
    public bool Enabled { get; set; }
    public string Executable { get; set; } = "qemu-system-x86_64";
    public bool UseKvm { get; set; }
    // Operator-owned, prepared Linux desktop qcow2 images with qemu-guest-agent and a non-root workflow user.
    public Dictionary<string, string> Images { get; set; } = new();
    public QemuAgentOptions Agent { get; set; } = new();
}
