using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public sealed class VirtualMachineChainActionTests
{
    [Fact]
    public void Configuration_is_bounded_and_defaults_to_no_network()
    {
        var action = new VirtualMachineChainAction("browser-linux", "cat");
        Assert.False(action.AllowNetwork);
        Assert.Throws<ArgumentException>(() => new VirtualMachineChainAction("/etc/passwd", "cat"));
        Assert.Throws<ArgumentException>(() => new VirtualMachineChainAction("browser", " "));
        Assert.Throws<ArgumentException>(() => new VirtualMachineChainAction("browser", "cat", "a.b"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualMachineChainAction("browser", "cat", cpus: 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualMachineChainAction("browser", "cat", memoryMb: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualMachineChainAction("browser", "cat", timeoutSeconds: 0));
    }
}
