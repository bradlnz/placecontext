namespace PlaceContext.Host.Components.ViewModels;

public sealed class JobTestEditorFile
{
    public JobTestEditorFile(string path, string content)
        => (Path, Content) = (path, content);

    public string Path { get; set; }
    public string Content { get; set; }
}
