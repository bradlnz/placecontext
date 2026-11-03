using PlaceContext.Application.Mcp;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Mcp;

namespace PlaceContext.Infrastructure.Mcp;

public sealed class HttpMcpConnectionProbe(IHttpClientFactory http) : IMcpConnectionProbe
{
    public async Task<string> ProbeAsync(McpConnection conn, CancellationToken ct = default)
    {
        var status = "unknown";
        try
        {
            async Task ProbeTransportAsync()
            {
                if (conn.Transport is McpTransport.Http or McpTransport.Sse && !string.IsNullOrEmpty(conn.EndpointUrl))
                {
                    using var client = http.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(10);

                    // Apply auth headers
                    void AuthenticateRequest()
                    {
                        if (conn.AuthType is McpAuthType.Bearer or McpAuthType.OAuth && !string.IsNullOrEmpty(conn.AuthToken))
                        {
                            client.DefaultRequestHeaders.Authorization = new("Bearer", conn.AuthToken);
                            return;
                        }
                        if (conn.AuthType == McpAuthType.ApiKey && !string.IsNullOrEmpty(conn.AuthToken))
                        {
                            client.DefaultRequestHeaders.Add("X-API-Key", conn.AuthToken);
                            return;
                        }
                        if (conn.AuthType == McpAuthType.Header && !string.IsNullOrEmpty(conn.AuthHeader) && !string.IsNullOrEmpty(conn.AuthToken))
                        {
                            client.DefaultRequestHeaders.Add(conn.AuthHeader, conn.AuthToken);
                        }
                    }
                    AuthenticateRequest();
                    using var resp = await client.GetAsync(conn.EndpointUrl, ct);
                    status = resp.IsSuccessStatusCode ? "connected" : $"http:{(int)resp.StatusCode}";
                    return;
                }
                status = "stdio:pending";
            }
            await ProbeTransportAsync();
        }
        catch (Exception ex)
        {
            status = $"error:{ex.Message[..Math.Min(50, ex.Message.Length)]}";
        }

        return status;
    }
}
