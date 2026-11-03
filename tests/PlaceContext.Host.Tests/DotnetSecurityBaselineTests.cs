using System.Text.Json;

namespace PlaceContext.Host.Tests;

public sealed class DotnetSecurityBaselineTests
{
    [Fact]
    public void Build_and_container_versions_include_the_October_25_security_update()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PlaceContext.slnx")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        using var sdk = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "global.json")));
        Assert.True(Version.Parse(sdk.RootElement.GetProperty("sdk").GetProperty("version").GetString()!) >= new Version(10, 0, 403));
        Assert.False(sdk.RootElement.GetProperty("sdk").GetProperty("allowPrerelease").GetBoolean());
        var docker = File.ReadAllText(Path.Combine(root.FullName, "Dockerfile"));
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0.403", docker);
        Assert.Contains("mcr.microsoft.com/dotnet/aspnet:10.0.14", docker);
        Assert.True(Environment.Version >= new Version(10, 0, 14));
    }
}
