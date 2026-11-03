using System.Net;
using PlaceContext.Infrastructure.Storage;

namespace PlaceContext.Infrastructure.Tests;

public sealed class HttpPresignedUploadClientTests
{
    [Fact]
    public async Task Non_seekable_upload_sends_explicit_length_and_content_type()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        using var stream = new NonSeekableStream([1, 2, 3]);
        await new HttpPresignedUploadClient(new Factory(client)).UploadAsync("https://storage/upload", stream, 3, "application/pdf");
        Assert.True(handler.Called);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public bool Called
        {
            get; private set;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Called = true;
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(3, request.Content!.Headers.ContentLength);
            Assert.Equal("application/pdf", request.Content.Headers.ContentType!.MediaType);
            Assert.Equal(new byte[] { 1, 2, 3 }, await request.Content.ReadAsByteArrayAsync(ct));
            return new(HttpStatusCode.OK);
        }
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
