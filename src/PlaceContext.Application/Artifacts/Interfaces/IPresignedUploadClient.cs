namespace PlaceContext.Application.Artifacts;

public interface IPresignedUploadClient
{
    Task UploadAsync(string url, Stream stream, long length, string contentType, CancellationToken ct = default);
}
