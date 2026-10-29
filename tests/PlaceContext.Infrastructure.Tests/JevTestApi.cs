namespace PlaceContext.Infrastructure.Tests;

public sealed class JevTestApi(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler, IHttpClientFactory
{
    public int Requests { get; private set; }
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        return send(request, ct);
    }
}
