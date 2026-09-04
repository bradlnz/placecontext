namespace PlaceContext.Host.Components.ViewModels;

public static class EditorLanguageCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Languages = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        [".js"] = "javascript",
        [".cjs"] = "javascript",
        [".mjs"] = "javascript",
        [".ts"] = "typescript",
        [".py"] = "python",
        [".go"] = "go",
        [".rb"] = "ruby",
        [".cs"] = "csharp",
        [".json"] = "json",
        [".sh"] = "shell",
        [".md"] = "markdown",
        [".html"] = "html",
        [".css"] = "css",
        [".yml"] = "yaml",
        [".yaml"] = "yaml",
    };

    public static string ForPath(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        return Languages.GetValueOrDefault(extension, "plaintext");
    }
}
