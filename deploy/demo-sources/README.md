# Local demo sources

From the repository root, with Docker, Docker Compose, Python 3, and OpenSSL installed:

```bash
python3 deploy/demo-sources/setup.py
```

This starts five source services plus an S3-compatible artifact store. Ports bind to localhost, data lives in named
Docker volumes, and generated passwords/certificates stay in the ignored `dist/demo-source/`
directory. Keep that directory when stopping or recreating containers. SQL Server uses its
Developer edition for local development; the stack has about 3.5 GiB of container memory limits.

| Source | Host port | Seed data |
| --- | --- | --- |
| PostgreSQL | 5434 | Nine related ecommerce tables, including 200 orders |
| MySQL | 3307 | 200 orders with customer names, cities, channels, and totals |
| SQL Server | 1434 | The same 200 orders |
| RabbitMQ | 5673; management UI 15673 | 25 JSON events in durable queue `demo.orders` |
| Redis | 6380 | 25 JSON events in stream `demo:orders`, with AOF persistence |
| SeaweedFS S3 | 8333 | Encrypted run artifacts, reports and charts |

The database is `source_demo`; all three SQL engines have a `source_reader` login with SELECT
permissions. Passwords and exact connection details are in `dist/demo-source/connections.json`.
Prices are integer AUD cents. Data is fictional, anchored to 4 November 2026, and every table
is below the importer’s 1,000-row snapshot limit. PostgreSQL also contains customers, products,
order items, payments, shipments, returns, inventory, and support tickets.

## Connect from PlaceContext

Restart the local app using `./run.sh` after setup. The launcher starts the artifact container
and loads private credentials from `dist/demo-source/object-store.env` unless an external
`PlaceContext__ObjectStore__Endpoint` is already set. Keep that file and the `artifacts` volume.
The store uses pinned SeaweedFS 4.52 through the existing S3 adapter; only its S3 port is exposed
on localhost. See [SeaweedFS setup](https://github.com/seaweedfs/seaweedfs/wiki/Quick-Start-with-weed-mini).
The launcher loads the demo certificate
alongside the system CA bundle for this process; it does not change system trust or disable TLS
verification. An already-running app must be stopped first because the launcher exits when it
detects a healthy instance. If you explicitly set `SSL_CERT_FILE` or `PGSSLROOTCERT`, the launcher
preserves those settings, so include the demo certificate in your configured trust store.

In **Data sources**, choose PostgreSQL, MySQL, or SQL Server, enter `localhost`, the port above,
database `source_demo`, user `source_reader`, and the password from the connection file. Click
**Connect & discover data**, choose `orders`, and retain `order_id` as the unique key.
Schemas are `public` for PostgreSQL, `source_demo` for MySQL, and `dbo` for SQL Server.

RabbitMQ and Redis are populated integration fixtures. The app currently has no direct source
connector for either; consuming them into PlaceContext needs a job or adapter that submits to
the existing feed/queue APIs. No such consumer is started by this setup.

The demo TLS certificate lasts one year. TLS configuration follows the engines’ native settings:
[PostgreSQL SSL](https://www.postgresql.org/docs/17/ssl-tcp.html),
[MySQL SSL](https://dev.mysql.com/doc/refman/8.4/en/using-encrypted-connections.html), and
[SQL Server TLS configuration](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-configure-mssql-conf#specify-tls-settings).

## Where the records go

Source imports are encrypted snapshots in the application's PostgreSQL `source_documents`
table, available in Sources for preview and search. They do not automatically become project
SQL tables. The configured order pipeline uses PlaceContext's durable PostgreSQL workflow
queue; RabbitMQ and Redis remain upstream demo sources.

The local Default project has this pipeline configured through the UI and authenticated APIs:

```text
Five order sources → demo-orders-classification queue → Validate orders
  → Built-in AI classification → classification.noul >= 0.9?
      true: Organize classified orders → classified_orders
      false: Hold orders for review → order_classification_review
  → If summary.needs_review > 0: Human in the loop (await a decision)
```

The source gate checks required fields before queueing. The validation job checks required
fields, JSON types, and unique order IDs within each batch. Its output also maps to
`validated_orders` as staging data. The built-in AI assesses whether every record fits the
defined taxonomy; the mapping job applies its group/tag rules after the AI condition:

| Table | Contents |
| --- | --- |
| `validated_orders` | Orders that passed structural validation, before agent classification |
| `classified_orders` | Accepted groups: active orders, completed orders, cancellations, refunds |
| `order_classification_review` | Batches with noul below 0.9 or unsupported record values |

Grouped rows preserve the original record, source ID/name, tags, reason, confidence, taxonomy
version, and mapping/run provenance. Data maps create tables in the project's isolated SQL
schema. The agent cannot replace the original order's prices, identifiers, or provenance.

The agent step uses the built-in AI provider (`jev`) automatically with
[`order_groups_prompt.txt`](../../scripts/demo/order_groups_prompt.txt). It returns the model,
question, and `noul` score in `classification`. The workflow condition is
`gte:classification.noul:0.9`, with an explicit false branch. Both mapping jobs use
[`map_order_groups.py`](../../scripts/demo/map_order_groups.py); the false branch forces every
record into review. Missing or malformed score evidence cannot produce accepted rows. Provider
errors fail the workflow; no external-agent response or manual resume is needed. Review sends
the complete JSON as text and uses the configured criteria directly, avoiding structured-field
token overhead and a second model call to rewrite the question. No records are truncated.

The existing `confidence` column stores the AI's batch-level noul score; the reason includes
the model and threshold, and job artifacts retain the full score metadata. This is not a
per-record calibrated accuracy measurement. Taxonomy `orders-v2` distinguishes these runs from
the earlier external-agent demonstration. Review rows require a later decision and are never
automatically approved. A low score holds the whole batch rather than guessing which row failed.

Runtime IDs, source links, and demonstration receipts are saved in the ignored
`dist/demo-source/validation-pipeline.json`. The setup script above provisions the upstream
fixtures only; it does not recreate these application workflows or data maps in a new database.
The non-order sources remain available in Sources and need their own classification contracts.

Example analysis from the project's Data tab:

```sql
SELECT group_name, count(*) AS records, sum(total_cents) / 100.0 AS total_aud
FROM classified_orders
WHERE input_source_name = 'Demo PostgreSQL · orders' AND taxonomy_version = 'orders-v2'
GROUP BY group_name
ORDER BY group_name
```

Mappings append run outputs; they do not upsert orders or remove past rows on source deletion.
Repeated processing can therefore create multiple versions. Unchanged source syncs do not
queue records again. The demo engines intentionally contain overlapping orders: filter by
source and choose the latest version per source/order before calculating production totals.
Queue receipts use one attempt, a one-day waiting TTL, and 30-day retention. Failed batches
remain inspectable and can be retried manually. Deletion-only batches need a separate contract.

## Stop, inspect, and reseed

```bash
docker compose --env-file dist/demo-source/.env -f deploy/demo-sources/compose.yaml ps
docker compose --env-file dist/demo-source/.env -f deploy/demo-sources/compose.yaml stop
python3 deploy/demo-sources/setup.py
```

Setup preserves existing database rows, queues, streams, and passwords. It restarts the MySQL
and SQL Server demo containers to apply TLS settings. Existing queues and streams are not
repopulated after consumption; explicitly remove only the demo queue/stream if you want a fresh
set of events. Keep volumes to retain edits. The normal `run.sh` starts the configured artifact
container but does not start the optional source engines.

```bash
python3 -m unittest discover -s scripts/demo -p 'test_*.py'
python3 tests/deploy/test_run.py
```

## Parallel load test and TypeSafe limits

The target of 100,000–1,000,000 records/hour is approximately 28–278 records/second.
A batch is one tracked workflow submission, not one submission per record. The queue
scheduler continuously drains submissions; `PlaceContext:Scheduling:MaxConcurrentChains`
controls concurrent workflows (default 2). The current Python runtime starts a Kubernetes
job for each validation/mapping step. Persistent native/Rust runner code exists but is not
wired into this runner and does not execute these Python jobs.

Before enabling AI traffic, configure `PlaceContext:TypeSafe:RequestsPerSecond` and
`PlaceContext:TypeSafe:TokensPerSecond` using the organization's current developer-console
limits, reduced by capacity allocated to other applications. Both default to zero, which
blocks outbound AI calls until configured. These are organization limits, not per-key or
per-worker allowances. Every application replica sharing an organization must use the same
application database, `RateLimitScope`, and limits. The default scope conservatively shares
one allowance across all TypeSafe credentials used by this deployment.

The named TypeSafe HTTP client uses PostgreSQL to enforce a shared rolling one-second
request/token budget. Each text request reserves its UTF-8 body size plus 1,024 as a
conservative token estimate; this intentionally trades utilization for headroom and is not
a provider tokenizer. A request larger than the configured token budget is rejected before
sending: reduce its batch size. Image requests also reserve their full encoded body size;
this is not a calibrated image-token estimator. `429` and `529` responses set a shared
cooldown and retry at most three times, using exponential backoff with jitter and honoring
both delta-seconds and HTTP-date `Retry-After`. Existing request deadlines still apply;
long provider outages fail closed rather than approving data. Other applications outside
this database must have separately allocated capacity. See the current official
[limits](https://docs.typesafe.ai/models) and [retry rules](https://docs.typesafe.ai/api/errors).

Seed the separate, deterministic 100,000-record fixture without modifying the original
source tables:

```bash
docker exec -i placecontext-source-demo psql -U postgres -d source_demo -v ON_ERROR_STOP=1 < deploy/demo-sources/seed_load_test.sql
```

After configuring limits and restarting the host, use a temporary personal API token file
(mode 600) with the existing demo workflow:

```bash
python3 scripts/demo/load_orders.py --end 100 --batch-size 10 --inflight 4 --phase baseline --token-file /path/to/token
python3 scripts/demo/load_orders.py --end 1100 --batch-size 100 --inflight 4 --phase batch100 --token-file /path/to/token
python3 scripts/demo/load_orders.py --end 100000 --batch-size 250 --inflight 8 --phase bulk --token-file /path/to/token
```

The last command is an example, not a measured capacity recommendation. Increase host
concurrency only after measuring CPU/memory, AI throttling, and persisted results. The
loader reads bounded PostgreSQL pages with a read-only role, enqueues through the normal
authenticated API, and checkpoints ranges, content-based idempotency keys, receipts, and
phase timestamps in `dist/demo-source/load-test.json`. Restarting with the same checkpoint
resumes unfinished submissions. It stops adding work on workflow failures or unexpected
manual-input waits. Do not delete that checkpoint to retry: mappings are append-only.

The 250-row mapper limit and loader's 60,000-character context guard leave room under the
application's 65,536-character AI-state limit. Direct-loader records use `input_source_id =
'load-test-orders-v1'` and do not pass through the Sources importer.

For source-driven processing, connect PostgreSQL table `public.orders_load_test` in
**Data → Sources**, select `order_id` as the key and the five fixture columns, connect the
classification queue, and set **Records per batch** to 250. Queued PostgreSQL sources accept
up to 100,000 records / 32 million characters. A sync commits the encrypted snapshot and
queue messages atomically; unchanged resyncs do not enqueue again. Source-driven outputs use
the source UUID. Stop the direct loader first and filter by source when comparing test runs.

Verify exact counts and distinct order IDs across
`classified_orders` and `order_classification_review`, plus structural validation counts,
before claiming full ingestion. Mapping errors currently do not fail a workflow receipt.

At 250 records/request, 1M/hour needs about 1.11 successful AI requests/second, excluding
retries. Token consumption and end-to-end latency can impose lower ceilings. A batch-level
noul check followed by deterministic status grouping is the current demo contract; it is
not a test of independent semantic classification of every record. No 100k–1M/hour result
has yet been established. Current scale concerns include per-step container startup,
schema locks during mapping,
append-only retry semantics, and the snapshot importer's whole-source materialization.

10,000 records/second requires 40 successful 250-record reviews/second, plus retries and
enough workflow capacity to keep the reviewer busy. The development host uses twelve concurrent workflows and retains
the shared 1-request/second TypeSafe budget. Multiple host replicas can claim distinct queue
messages atomically, but must share the database, encryption keys, rate-limit scope and provider
allocation. Raising worker count cannot bypass that allowance. Confirm organization request
and token limits before increasing them; no 10k/second throughput has been demonstrated.

Held batches pause in the workflow inbox for a human decision. Decisions are recorded in
`human_review_decision`; held rows remain in the review table for audit. TypeSafe throttling
uses shared HTTP backoff and up to three durable workflow retries before failing.
