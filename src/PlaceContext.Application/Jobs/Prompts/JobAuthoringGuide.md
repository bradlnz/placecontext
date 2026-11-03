# Authoring a PlaceContext job

A job is **map** code (optionally a **reduce** step) run as isolated, sandboxed containers.

## The sandbox contract
- **Input** arrives on **STDIN**: the shard's input payload (one run per payload in the job's
  `inputPayloads`). Read all of stdin and parse it (usually JSON). With no payloads you get one
  run with `{}`.
- **Working dir**: your files are mounted **read-only at `/work`**; the entrypoint is invoked as
  `/work/<entrypoint>`.
- **Output**: write your result to **STDOUT** — it's captured as the shard's artifact. Keep logs
  on STDERR so they don't pollute the result.
- **Exit codes**: `0` = success (configurable). Non-success codes can be mapped to Partial.
- **No network** by default (sealed sandbox). Set the job's network-egress policy to allow it.

## Runtimes
**Default to `python` unless the user asks for another language** — it reads best for the
data-shaping work jobs do, and its stdlib covers json/csv/dates without dependencies.
- `python`→ base `python:3.12-slim`, default entrypoint `main.py`, invoked `python /work/main.py`. **(default)**
- `node`  → base `node:22-slim`, default entrypoint `index.js`, invoked `node /work/index.js`.
- `go`    → base `golang:1.23-alpine`, default entrypoint `main.go`, invoked `go run /work/main.go`.
- `ruby`  → base `ruby:3.3-slim`, default entrypoint `main.rb`, invoked `ruby /work/main.rb`.
- `dotnet`→ base `mcr.microsoft.com/dotnet/sdk:10.0.403`, default entrypoint `main.cs`, invoked `dotnet run /work/main.cs` (.NET 10 file-based app). The first run is slower (an implicit
  restore is baked once and reused afterwards). File-based apps disable reflection-based
  `System.Text.Json` by default — use a source-generated `JsonSerializerContext`, or add
  `#:property JsonSerializerIsReflectionEnabledByDefault=true` at the top of the file.
  `#:property`/`#:package` directives are supported; `#:package` downloads need network egress.

## Dependencies
Ship your runtime's manifest as an extra file and packages install before the entrypoint runs:
`requirements.txt` (pip), `package.json` (npm), `Gemfile` (bundler), `go.mod` (go modules).
Downloads need the job's network-egress policy set to allow — the sealed sandbox is never
relaxed implicitly. `dotnet` has no manifest step; the runtime packs it needs are baked into
a warm image, and extra packages come from `#:package` directives (those downloads need
network egress).

## Environment variables & secrets
Plain config goes in the job's `env`. **Secrets/credentials come from the encrypted vault** —
reference them by name (managed in the portal; encrypted at rest). They are injected as env vars
into the sandbox at run time; never hard-code credentials in the code.

## The artifact IS the point
Jobs exist to generate artifacts. Emit **JSON** on stdout, and when the result is a numeric
series (e.g. `[{"day":"mon","total":12}, …]` or `{"mon":12,"tue":31}`) the portal
chart it automatically — in the run detail and the global Reports view.

## Files & binary artifacts (PDFs, images, CSVs)
Two channels, both binary-safe end to end:
- **Write files to `/out`** — every file is captured as a named artifact.
- **Embed them in your stdout JSON** — `"artifacts": [{"filename": "report.pdf",
  "base64": "…"}]` (use `"content"` for text files). Use this when writing files isn't an
  option (e.g. image workloads in-cluster).
Emitted PDFs, HTML, and images publish automatically as openable portal links on the run.

## Examples
python `main.py` (the default runtime):
```py
import sys, json, os
data = json.loads(sys.stdin.read() or "{}")
api_key = os.environ.get("API_KEY")            # from the vault
print(json.dumps({"count": len(data.get("items", []))}))
```
node `index.js`:
```js
const input = JSON.parse(require('fs').readFileSync(0, 'utf8') || '{}');
const apiKey = process.env.API_KEY;            // from the vault
const result = { count: (input.items ?? []).length };
process.stdout.write(JSON.stringify(result));  // the artifact
```

## Next step
Upload with **upload_job_code**: `filesJson` = [{"path":"main.py","content":"…"}], `runtimeId`
(defaults to "python"; also "node"|"go"|"ruby"|"dotnet"), and `entrypoint` when uploading
multiple files.