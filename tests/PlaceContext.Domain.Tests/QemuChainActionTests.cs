using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Tests;

public sealed class QemuChainActionTests
{
    [Fact]
    public void Configuration_is_bounded_and_defaults_to_no_network()
    {
        var action = new QemuChainAction("browser-linux", "cat");
        Assert.False(action.AllowNetwork);
        Assert.Throws<ArgumentException>(() => new QemuChainAction("/etc/passwd", "cat"));
        Assert.Throws<ArgumentException>(() => new QemuChainAction("browser", " "));
        Assert.Throws<ArgumentException>(() => new QemuChainAction("browser", "cat", "a.b"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QemuChainAction("browser", "cat", cpus: 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QemuChainAction("browser", "cat", memoryMb: 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QemuChainAction("browser", "cat", timeoutSeconds: 0));
    }
}
