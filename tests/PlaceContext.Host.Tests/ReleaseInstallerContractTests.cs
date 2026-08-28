namespace PlaceContext.Host.Tests;

public sealed class ReleaseInstallerContractTests
{
    [Fact]
    public void Web_host_ships_the_verified_github_release_installer()
    {
        var installerPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "install.sh");

        Assert.True(File.Exists(installerPath), "The published web host must include /install.sh.");
        var installer = File.ReadAllText(installerPath);
        Assert.Contains(
            "github.com/bradlnz/placecontext/releases",
            installer,
            StringComparison.Ordinal
        );
        Assert.Contains("latest/download", installer, StringComparison.Ordinal);
        Assert.Contains("download/v$VERSION", installer, StringComparison.Ordinal);
        Assert.Contains(
            "placecontext-deploy-$arch.tar.gz",
            installer,
            StringComparison.Ordinal
        );
        Assert.Contains("verify_sha256", installer, StringComparison.Ordinal);
        Assert.Contains("validate_archive", installer, StringComparison.Ordinal);
        Assert.Contains("placecontext-ai", installer, StringComparison.Ordinal);
        Assert.Contains("--ai-token", installer, StringComparison.Ordinal);
        Assert.Contains("placecontext-runtime.tar", installer, StringComparison.Ordinal);
        Assert.Contains("k3d image import", installer, StringComparison.Ordinal);
        Assert.Contains("deploy_local", installer, StringComparison.Ordinal);
    }
}
