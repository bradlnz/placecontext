using System.Net;
using System.Text.Json;
using System.Text;

namespace PlaceContext.Infrastructure.Tests.Workload.TypeSafe;

internal sealed class ModelReviewHttp : HttpMessageHandler, IHttpClientFactory
{
    public string? Authorization
    {
        get; private set;
    }
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Authorization = request.Headers.Authorization?.Parameter;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { model = "jev-test", answers = new { verification = new { type = "noul", noul = .95 } } }), Encoding.UTF8, "application/json")
        });
    }
}
