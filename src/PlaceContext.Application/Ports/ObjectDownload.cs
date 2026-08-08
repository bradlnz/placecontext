namespace PlaceContext.Application.Ports;

/// <summary>A readable object: its content stream plus the stored content type. Dispose to release.</summary>
public sealed record ObjectDownload(Stream Content, string ContentType) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
