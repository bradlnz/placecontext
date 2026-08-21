# Webhook and API ingestion

*Push JSON into PlaceContext through an authenticated endpoint and run event-triggered jobs in any supported language.*

## Enable the endpoint

Set a high-entropy key on the PlaceContext deployment and restart or roll the host:

```text
PlaceContext__Ingest__Key=<random-secret>
```

The endpoint is disabled with HTTP `404` until this value is configured. Keep the key in the
sending service's secret store; do not put it in source control or a browser application.

Send `POST /api/ingest/{eventName}` (the original `/ingest/{eventName}` route remains supported).
Authenticate with one of these equivalent request forms:

- `X-Ingest-Key: <key>` — recommended for webhook senders;
- `Authorization: Bearer <key>` — standard HTTP clients;
- `X-Api-Key: <key>` — clients already using the PlaceContext API convention.

The optional `projectId` query parameter limits delivery to one project. Bodies are passed unchanged
to matching Event-triggered jobs as stdin and may be up to 1 MiB. A successful request returns the
event name, the triggered runs, and occurrence time.

## Connect a job

1. Create a job from the **Webhook receiver** template.
2. Choose Node, Python, Go, Ruby, or .NET; each language has a complete JSON payload handler.
3. Add an **Event** trigger to the job and enter the same event name used in the URL.
4. Send a test request and inspect the run's JSON artifact.

The public endpoint authenticates before emitting an event. Do not validate a request header inside
the job: only the body becomes job input. If a third-party provider cannot send one of the supported
key headers, place a small provider-specific signature-verifying adapter in front of this endpoint.

## Request examples

All examples send the same JSON event. Replace the base URL, event name, and environment-held key.

### Node.js

```javascript
const response = await fetch(`${process.env.PLACECONTEXT_URL}/api/ingest/order.received`, {
  method: "POST",
  headers: {
    "Authorization": `Bearer ${process.env.PLACECONTEXT_INGEST_KEY}`,
    "Content-Type": "application/json"
  },
  body: JSON.stringify({ orderId: "ord_123", total: 42.50 })
});
if (!response.ok) throw new Error(`PlaceContext ${response.status}: ${await response.text()}`);
console.log(await response.json());
```

### Python

```python
import os
import requests

response = requests.post(
    f"{os.environ['PLACECONTEXT_URL']}/api/ingest/order.received",
    headers={"X-Ingest-Key": os.environ["PLACECONTEXT_INGEST_KEY"]},
    json={"orderId": "ord_123", "total": 42.50},
    timeout=30,
)
response.raise_for_status()
print(response.json())
```

### Go

```go
payload := strings.NewReader(`{"orderId":"ord_123","total":42.50}`)
req, err := http.NewRequest(http.MethodPost,
    os.Getenv("PLACECONTEXT_URL")+"/api/ingest/order.received", payload)
if err != nil { log.Fatal(err) }
req.Header.Set("X-Ingest-Key", os.Getenv("PLACECONTEXT_INGEST_KEY"))
req.Header.Set("Content-Type", "application/json")
res, err := http.DefaultClient.Do(req)
if err != nil { log.Fatal(err) }
defer res.Body.Close()
if res.StatusCode < 200 || res.StatusCode >= 300 { log.Fatalf("PlaceContext: %s", res.Status) }
```

### Ruby

```ruby
require "json"
require "net/http"

uri = URI("#{ENV.fetch('PLACECONTEXT_URL')}/api/ingest/order.received")
request = Net::HTTP::Post.new(uri)
request["X-Ingest-Key"] = ENV.fetch("PLACECONTEXT_INGEST_KEY")
request["Content-Type"] = "application/json"
request.body = JSON.generate(orderId: "ord_123", total: 42.50)
response = Net::HTTP.start(uri.host, uri.port, use_ssl: uri.scheme == "https") { |http| http.request(request) }
raise "PlaceContext #{response.code}: #{response.body}" unless response.is_a?(Net::HTTPSuccess)
```

### .NET (C#)

```csharp
using System.Net.Http.Json;

using var client = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("PLACECONTEXT_URL")!) };
client.DefaultRequestHeaders.Add("X-Ingest-Key", Environment.GetEnvironmentVariable("PLACECONTEXT_INGEST_KEY"));
using var response = await client.PostAsJsonAsync("/api/ingest/order.received", new { orderId = "ord_123", total = 42.50m });
response.EnsureSuccessStatusCode();
Console.WriteLine(await response.Content.ReadAsStringAsync());
```

## Responses and retries

| Status | Meaning |
|---|---|
| `200` | Event accepted; response lists the runs that were triggered |
| `400` | Event name is blank or longer than 200 characters |
| `401` | Key is missing or incorrect |
| `404` | Ingestion is not enabled on this deployment |
| `413` | Body exceeds 1 MiB |
| `429` | Sender exceeded the ingestion rate limit |

Retry network failures and `429`/`5xx` responses with exponential backoff. A repeated event can start
another run, so include a stable source event ID in the JSON and make downstream processing
idempotent when the sender may retry after an uncertain response.
