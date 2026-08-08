# PlaceContext App gateway

`PlaceContext.App` is the permanent edge for the React application. `PlaceContext.Host` is migration
scaffolding and is not part of the target runtime.

The Vite production build emits to `wwwroot/app`. App serves those fingerprinted assets and uses an
anonymous SPA fallback for `/app` routes. App does not register the microservice runtime or require
`PlaceContext:ServiceAuth` merely to start. The destination service validates the forwarded bearer
token/API key or explicitly permits an anonymous route such as public ingestion or an artifact share.

The API has two responsibilities:

- stream single-service operations to the owning microservice with the caller's bearer token and
  request correlation headers intact;
- compose React page reads that span multiple services, without taking ownership of their domain
  rules or persistence.

It must not reference `PlaceContext.Host`, a service implementation project, a service infrastructure
project, or a service database. Cross-service pages such as Dashboard, Workspace Overview, and the
project graph are API compositions over service HTTP contracts rather than reasons to reassemble the
old monolith in this process.

Configure upstream origins under `PlaceContext:Microservices:Destinations`. For example:

```json
{
  "PlaceContext": {
    "Microservices": {
      "Destinations": {
        "AgentChat": "http://placecontext-agent-chat:8080",
        "Agents": "http://placecontext-agents:8080",
        "Artifacts": "http://placecontext-artifacts:8080",
        "Crm": "http://placecontext-crm:8080",
        "Data": "http://placecontext-data:8080",
        "Jobs": "http://placecontext-jobs:8080",
        "Search": "http://placecontext-search:8080",
        "Vault": "http://placecontext-vault:8080"
      }
    }
  }
}
```

The equivalent Jobs environment variable is
`PlaceContext__Microservices__Destinations__Jobs=http://placecontext-jobs:8080`.

An owned route with no valid absolute HTTP(S) destination returns `503`. A connection failure to a
configured destination returns `502`. The proxy does not retry requests because several service
routes perform non-idempotent writes.

## Host removal gate

The legacy Host can be deleted when:

1. React owns every route in `frontend/src/app/host-route-catalog.ts`.
2. This project serves the React production assets and owns browser authentication/session handling.
3. Every React `/api/v1` contract has either moved to an API composition or maps to a service-owned
   proxy route.
4. No frontend link, deployment manifest, project reference, or test depends on `PlaceContext.Host`.

During migration, an endpoint remaining in the Host is unfinished migration work, not a supported
gateway fallback. The API proxy deliberately has no `LegacyHost` destination.
