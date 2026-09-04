using System.Net.Http;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class TestMcpConnectionHandler : ICommandHandler<TestMcpConnectionCommand, McpConnectionView>
{
    private readonly IMcpConnectionRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IHttpClientFactory _http;

    public TestMcpConnectionHandler(IMcpConnectionRepository repo, IUnitOfWork uow, IClock clock, IHttpClientFactory http)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
        _http = http;
    }

    public async Task<McpConnectionView> HandleAsync(TestMcpConnectionCommand command, CancellationToken ct = default)
    {
        var conn = await _repo.GetByIdAsync(command.Id, ct) ?? throw new InvalidOperationException($"McpConnection {command.Id} not found.");

        var status = "unknown";
        try
        {
            if (conn.Transport is McpTransport.Http or McpTransport.Sse && !string.IsNullOrEmpty(conn.EndpointUrl))
            {
                var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                // Apply auth headers
                if (conn.AuthType is McpAuthType.Bearer or McpAuthType.OAuth && !string.IsNullOrEmpty(conn.AuthToken))
                {
                    client.DefaultRequestHeaders.Authorization = new("Bearer", conn.AuthToken);
                }
                else if (conn.AuthType == McpAuthType.ApiKey && !string.IsNullOrEmpty(conn.AuthToken))
                {
                    client.DefaultRequestHeaders.Add("X-API-Key", conn.AuthToken);
                }
                else if (conn.AuthType == McpAuthType.Header && !string.IsNullOrEmpty(conn.AuthHeader) && !string.IsNullOrEmpty(conn.AuthToken))
                {
                    client.DefaultRequestHeaders.Add(conn.AuthHeader, conn.AuthToken);
                }

                var resp = await client.GetAsync(conn.EndpointUrl, ct);
                status = resp.IsSuccessStatusCode ? "connected" : $"http:{(int)resp.StatusCode}";
            }
            else
            {
                status = "stdio:pending";
            }
        }
        catch (Exception ex)
        {
            status = $"error:{ex.Message[..Math.Min(50, ex.Message.Length)]}";
        }

        conn.RecordConnection(status, _clock.UtcNow);
        await _repo.UpdateAsync(conn, ct);
        await _uow.SaveChangesAsync(ct);
        return McpConnectionMapper.ToView(conn);
    }
}
