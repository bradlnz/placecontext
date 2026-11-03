using System.Net;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Infrastructure.Mcp;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.TestSupport;

namespace PlaceContext.Infrastructure.Tests;

public sealed class McpConnectionProbeTests
{
    [Theory]
    [InlineData("bearer", "Authorization", "Bearer secret")]
    [InlineData("oauth", "Authorization", "Bearer secret")]
    [InlineData("apikey", "X-API-Key", "secret")]
    [InlineData("header", "X-Custom", "secret")]
    public async Task Http_probe_keeps_authentication_and_does_not_change_domain_state(
        string auth, string header, string expected)
    {
        using var http = new HttpStub(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://mcp.test/", request.RequestUri!.AbsoluteUri);
            Assert.Equal(expected, Assert.Single(request.Headers.GetValues(header)));
            return new(HttpStatusCode.OK);
        });
        var connection = Connection(auth: auth);

        Assert.Equal("connected", await new HttpMcpConnectionProbe(http).ProbeAsync(connection));
        Assert.Null(connection.LastStatus);
        Assert.Null(connection.LastConnectedAt);
    }

    [Fact]
    public async Task Command_records_and_commits_probe_result_with_the_application_clock()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeCurrentTenant(Guid.NewGuid()));
        var repository = new EfMcpConnectionRepository(db);
        var connection = Connection();
        await repository.AddAsync(connection);
        await db.SaveChangesAsync();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        using var http = new HttpStub(_ => new(HttpStatusCode.Unauthorized));
        var handler = new TestMcpConnectionHandler(repository, db, clock, new HttpMcpConnectionProbe(http));

        await handler.HandleAsync(new TestMcpConnectionCommand(connection.Id));

        db.ChangeTracker.Clear();
        var saved = await repository.GetByIdAsync(connection.Id);
        Assert.Equal("http:401", saved!.LastStatus);
        Assert.Equal(clock.UtcNow, saved.LastConnectedAt);
    }

    [Fact]
    public async Task Unsupported_transport_does_not_send_http_and_failures_keep_bounded_status()
    {
        using var http = new HttpStub(_ => throw new HttpRequestException(new string('x', 100)));
        var probe = new HttpMcpConnectionProbe(http);
        Assert.Equal("stdio:pending", await probe.ProbeAsync(Connection(transport: "stdio")));
        Assert.Equal(0, http.Requests);
        Assert.Equal("error:" + new string('x', 50), await probe.ProbeAsync(Connection()));
        Assert.Equal(1, http.Requests);
    }

    private static McpConnection Connection(string transport = "http", string auth = "none")
        => McpConnection.Create(Guid.NewGuid(), "Test", transport, "https://mcp.test", null, null,
            auth, "secret", "X-Custom", DateTimeOffset.UtcNow);

    private sealed class HttpStub(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler, IHttpClientFactory
    {
        public int Requests
        {
            get; private set;
        }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }
}
