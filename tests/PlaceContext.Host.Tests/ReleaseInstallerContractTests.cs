namespace PlaceContext.Host.Tests;

public sealed class ReleaseInstallerContractTests
{
    [Fact]
    public void Public_installer_runs_the_latest_github_release()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var installer = File.ReadAllText(Path.Combine(repositoryRoot, "install.sh"));

        Assert.Contains("releases/latest/download/install.sh", installer, StringComparison.Ordinal);
        Assert.Contains("bash -s -- \"$@\"", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_push_is_limited_to_the_owner_workflow()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github/workflows/release.yml"));

        Assert.Contains("github.actor == 'bradlnz'", workflow, StringComparison.Ordinal);
        Assert.Contains("      packages: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("  packages: write\n\nconcurrency:", workflow, StringComparison.Ordinal);
    }

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
        Assert.Contains("PLACECONTEXT_CLUSTER:-placecontext-local", installer, StringComparison.Ordinal);
        Assert.Contains("--api-port 127.0.0.1:0", installer, StringComparison.Ordinal);
        Assert.Contains("docker port \"k3d-$CLUSTER_NAME-serverlb\" 6443/tcp", installer, StringComparison.Ordinal);
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
