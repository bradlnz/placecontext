using System.Net.Http.Headers;
using PlaceContext.Application.Artifacts;
namespace PlaceContext.Infrastructure.Storage;

public sealed class HttpPresignedUploadClient(IHttpClientFactory clients) : IPresignedUploadClient
{
    public async Task UploadAsync(string url, Stream stream, long length, string contentType, CancellationToken ct = default)
    {
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        // BrowserFileStream is not seekable, so StreamContent cannot infer its length and
        // otherwise sends the presigned PUT using chunked transfer encoding. S3-compatible
        // stores such as DigitalOcean Spaces reject that request with HTTP 400.
        content.Headers.ContentLength = length;
        using var response = await clients
            .CreateClient()
            .PutAsync(url, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                string.IsNullOrWhiteSpace(details)
                    ? $"File storage returned {(int)response.StatusCode} ({response.ReasonPhrase})."
                    : $"File storage returned {(int)response.StatusCode} ({response.ReasonPhrase}): {details}",
                null,
                response.StatusCode
            );
        }
    }
}
