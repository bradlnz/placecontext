namespace PlaceContext.Host.Tests;

public sealed class ReleaseInstallerContractTests
{
    [Fact]
    public void Production_http_preserves_mcp_posts_when_redirecting_to_https()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var ingress = File.ReadAllText(Path.Combine(repositoryRoot, "deploy/release/k3s/production-ingress.yaml"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "src/PlaceContext.Host/Program.cs"));

        Assert.Contains("name: placecontext-http", ingress, StringComparison.Ordinal);
        Assert.Contains("router.entrypoints: web", ingress, StringComparison.Ordinal);
        Assert.Contains("Status308PermanentRedirect", program, StringComparison.Ordinal);
        Assert.Contains("app.UseHttpsRedirection()", program, StringComparison.Ordinal);
    }

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
            "placecontext-deploy.tar.gz",
            installer,
            StringComparison.Ordinal
        );
        Assert.Contains("verify_sha256", installer, StringComparison.Ordinal);
        Assert.Contains("validate_archive", installer, StringComparison.Ordinal);
        Assert.Contains("placecontext-ai", installer, StringComparison.Ordinal);
        Assert.Contains("--ai-token", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("k3d image import", installer, StringComparison.Ordinal);
        Assert.Contains("--api-port 127.0.0.1:0", installer, StringComparison.Ordinal);
        Assert.Contains("https://127.0.0.1:", installer, StringComparison.Ordinal);
        Assert.Contains("deploy_local", installer, StringComparison.Ordinal);
        Assert.Contains("raw.githubusercontent.com/docker/docker-install", installer, StringComparison.Ordinal);
        Assert.Contains("ROOTLESS_DOCKER_INSTALLER_COMMIT", installer, StringComparison.Ordinal);
        Assert.Contains("ROOTLESS_DOCKER_INSTALLER_SHA256", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("as_root", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("sudo ", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("]] || return\n", installer, StringComparison.Ordinal);
        Assert.Contains("printf '%q' \"$tmp\"", installer, StringComparison.Ordinal);
    }
}
