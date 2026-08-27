namespace PlaceContext.Host.Tests;

public sealed class ReleaseInstallerContractTests
{
    [Fact]
    public void Web_host_ships_the_github_release_installer()
    {
        var installerPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "install.sh");

        Assert.True(File.Exists(installerPath), "The published web host must include /install.sh.");
        var installer = File.ReadAllText(installerPath);
        Assert.Contains(
            "github.com/$REPOSITORY/releases/$tag_path",
            installer,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "placecontext-deploy.tar.gz",
            installer,
            StringComparison.Ordinal
        );
        Assert.Contains("verify_sha256", installer, StringComparison.Ordinal);
        Assert.Contains("deploy_local", installer, StringComparison.Ordinal);
    }
}
