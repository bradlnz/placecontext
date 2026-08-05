namespace PlaceContext.Host.Tests;

public sealed class CrmMobileUploadContractTests
{
    [Fact]
    public void Client_file_picker_stacks_on_mobile_with_a_full_width_touch_target()
    {
        var root = FindRepoRoot();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "Crm.razor")
        );
        var css = File.ReadAllText(
            Path.Combine(root, "src", "PlaceContext.Host", "Components", "Pages", "Crm.razor.css")
        );

        Assert.Contains("artifact-dropzone", page, StringComparison.Ordinal);
        Assert.Contains("dropzone-action", page, StringComparison.Ordinal);
        Assert.Contains("Choose file", page, StringComparison.Ordinal);
        Assert.Contains(
            ".artifact-dropzone {\n        align-items: stretch;\n        flex-direction: column;",
            css,
            StringComparison.Ordinal
        );
        Assert.Contains(
            ".dropzone-action {\n        width: 100%;\n        min-height: 44px;",
            css,
            StringComparison.Ordinal
        );
        Assert.Contains(
            ".dropzone-copy small {\n        white-space: normal;",
            css,
            StringComparison.Ordinal
        );
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "PlaceContext.Host")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
