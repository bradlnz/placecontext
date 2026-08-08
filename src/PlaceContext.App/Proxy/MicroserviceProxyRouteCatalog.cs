namespace PlaceContext.App.Proxy;

public static class MicroserviceProxyRouteCatalog
{
    public static IReadOnlyList<MicroserviceProxyRoute> All { get; } =
    [
        MicroserviceProxyRoute.ForPrefix("AgentChat", "/api/agent-chat"),
        MicroserviceProxyRoute.ForPrefix("Agents", "/api/v1/agents"),
        MicroserviceProxyRoute.ForPrefix("Artifacts", "/api/artifacts"),
        MicroserviceProxyRoute.ForPattern(
            "Artifacts",
            "^/runs/[^/]+/artifacts/[^/]+(?:/|$)"),
        MicroserviceProxyRoute.ForPrefix("Artifacts", "/share/artifacts"),
        MicroserviceProxyRoute.ForPrefix("Artifacts", "/chat/attachments"),
        MicroserviceProxyRoute.ForPrefix("Crm", "/api/crm"),
        MicroserviceProxyRoute.ForPrefix("Data", "/api/data"),
        MicroserviceProxyRoute.ForPrefix("Jobs", "/api/jobs"),
        MicroserviceProxyRoute.ForPrefix("Search", "/api/search"),
        MicroserviceProxyRoute.ForPattern(
            "Search",
            "^/api/v1/projects/[^/]+/opensearch(?:/|$)"),
        MicroserviceProxyRoute.ForPrefix("Vault", "/api/vault"),
    ];
}
