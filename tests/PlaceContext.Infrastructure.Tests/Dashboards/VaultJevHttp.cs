using System.Net;
using System.Text;
using System.Text.Json;

namespace PlaceContext.Infrastructure.Tests.Dashboards;

internal sealed class VaultJevHttp : HttpMessageHandler, IHttpClientFactory
{
    public string? Authorization { get; private set; }
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
