namespace PlaceContext.Application.Ports;

/// <summary>A readable object and its content type. Dispose to release the stream.</summary>
public sealed record ObjectDownload(Stream Content, string ContentType) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
