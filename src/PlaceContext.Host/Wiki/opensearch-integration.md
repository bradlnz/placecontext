# OpenSearch integration

*Connect a workspace or project to OpenSearch without exposing search credentials to browsers or jobs that do not need them.*

## What the integration provides

OpenSearch powers **Data → Search**, field discovery, bounded document search, aggregations, saved
dashboards, exports, and OpenSearch SQL. A project table can also be materialised into an index.
PlaceContext makes these requests server-side, so cluster credentials are never returned to the
browser.

You can configure one workspace default and override it per project:

| Scope | Configure in | Use when |
|---|---|---|
| Workspace default | Deployment environment | Several projects share one cluster |
| Project override | **Settings → Connections → External search index** | A project has its own cluster, credentials, or index pattern |

The project override wins. Resetting it returns that project to the workspace default.

## Decide whether to install it

OpenSearch is optional. Do not install it when PostgreSQL tables, CSV imports, MCP tools, and job
outputs cover the workspace's needs. Install or connect it when you need full-text document search,
aggregations across large indexes, search dashboards, or compatibility with an existing
OpenSearch/Elasticsearch data estate.

If the organisation already operates a reachable OpenSearch or compatible Elasticsearch endpoint,
reuse it with a dedicated PlaceContext service user and index scope. Otherwise, choose an
operator-managed deployment appropriate to the environment (for example a managed service or a
separately maintained cluster). PlaceContext deliberately does not start infrastructure from the
browser: installation changes cluster capacity, storage, networking, certificates, and backup
responsibilities and must remain an explicit operator action.

After the service is healthy, continue with **Prepare OpenSearch** and configure either the
workspace default or a project override below.

## Prepare OpenSearch

1. Create or select the indices PlaceContext may access.
2. Create a dedicated service user. Grant only the index patterns that belong to this workspace or
   project.
3. For read-only search, allow index listing, field capabilities, document search, and OpenSearch
   SQL. If users will materialise project tables, also allow index creation/deletion, bulk writes,
   and refresh on the permitted destination prefix.
4. Prefer HTTPS with a certificate trusted by the PlaceContext host. Do not disable certificate
   validation to work around an untrusted certificate.
5. Confirm the PlaceContext host—not the user's browser—can resolve and reach the endpoint.

OpenSearch security role names differ by distribution and version. Build the role around the
operations actually used: `/_cat/indices`, `/{index}/_field_caps`, `/{index}/_search`,
`/_plugins/_sql`, and, for materialisation, index `PUT`/`DELETE` plus `/_bulk`.

## Configure the workspace default

Set these on the PlaceContext host or deployment:

```text
PlaceContext__OpenSearch__Endpoint=https://search.example.com
PlaceContext__OpenSearch__Username=placecontext_reader
PlaceContext__OpenSearch__Password=<secret>
PlaceContext__OpenSearch__DefaultIndexPattern=project-*
```

Restart or roll the host after changing deployment environment variables. Leave username and
password empty only when the endpoint has another trusted server-side authentication layer.

## Configure one project

As a workspace administrator, open **Settings → Connections**, choose the project, and fill in
**External search index**. The endpoint must be an absolute `http://` or `https://` URL. The index
field accepts an exact index, alias, or permitted wildcard pattern.

The same values can be stored in the project's Vault under these names:

```text
OPENSEARCH_URL
OPENSEARCH_USERNAME
OPENSEARCH_PASSWORD
OPENSEARCH_INDEX
```

`OPENSEARCH_ENDPOINT` is accepted as a legacy alias for `OPENSEARCH_URL`. Resolved project values
are also made available to project jobs as the four canonical variables above, so a job can use the
same connection without copying credentials into its source.

## Verify the connection

1. Open **Data → Search** and confirm the expected indices appear.
2. Select an index and confirm fields load.
3. Run a narrow query and an aggregation.
4. If SQL is required, open the data studio and run a small `SELECT` against an index.
5. If materialisation is enabled, copy a disposable project table to a test index, search it, and
   remove the test index afterward.

An empty result is different from an unavailable connection. If the page reports OpenSearch as
unavailable, check the endpoint, host DNS, firewall/NetworkPolicy, credentials, certificate trust,
and the index pattern in that order.

## Optional manual collector sync

PlaceContext can call a small authenticated trigger that starts an operator-managed systemd
ingestion unit. This does not install or define your collector; it only asks an existing unit to
start.

On the collector host, ensure your ingestion unit exists, then install the trigger from a checkout:

```bash
OPENSEARCH_SYNC_HOST=admin@collector.example \
  deploy/opensearch-sync-trigger/install.sh
```

Edit `/etc/placecontext/opensearch-sync-trigger.env` on that host so it contains a random token and
the correct unit. Bind only to a private interface unless a TLS reverse proxy protects the service:

```text
SYNC_TRIGGER_TOKEN=<random-secret>
SYNC_TRIGGER_UNIT=opensearch-ingest.service
SYNC_TRIGGER_BIND=127.0.0.1
SYNC_TRIGGER_PORT=9340
```

If PlaceContext runs on another machine, use a private mesh address for `SYNC_TRIGGER_BIND`, restrict
port `9340` to the PlaceContext host, or expose the trigger through an authenticated HTTPS reverse
proxy. Then configure PlaceContext:

```text
PlaceContext__OpenSearch__SyncEndpoint=https://collector.example/v1/sync
PlaceContext__OpenSearch__SyncToken=<same-random-secret>
```

The trigger returns `202` when queued and `409` when the collector is already running. Its bearer
token is a high-value secret: store it with deployment secrets, never in the repository or a URL.

## Troubleshooting

| Symptom | Check |
|---|---|
| No indices | Index pattern and permission for `/_cat/indices` |
| Fields fail to load | Permission for `/_field_caps` and whether the pattern matches closed indices |
| Search works but SQL fails | OpenSearch SQL plugin and permission for `/_plugins/_sql` |
| Read works but materialisation fails | Create/delete/bulk privileges for the destination prefix |
| Host can connect locally but PlaceContext cannot | Container DNS, egress policy, proxy, and CA trust |
| Manual sync is unavailable | Both `SyncEndpoint` and `SyncToken` must be set |
| Sync returns conflict | The configured ingestion unit is already active |
