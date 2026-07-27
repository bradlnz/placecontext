namespace PlaceContext.Host.Components.ViewModels;

public sealed class UploadedFile
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
