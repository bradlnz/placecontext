namespace PlaceContext.Host.Tests;

public sealed class ReleaseInstallerContractTests
{
    [Fact]
    public void Web_host_ships_a_one_click_installer_for_spaces_zip_releases()
    {
        var installerPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "install.sh");

        Assert.True(File.Exists(installerPath), "The published web host must include /install.sh.");
        var installer = File.ReadAllText(installerPath);
        Assert.Contains(
            "https://placecontext.syd1.cdn.digitaloceanspaces.com",
            installer,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "ASSET=\"placecontext-${OS}-${ARCH}.zip\"",
            installer,
            StringComparison.Ordinal
        );
        Assert.Contains("extract_zip \"$TMP/pkg.zip\"", installer, StringComparison.Ordinal);
    }
}
