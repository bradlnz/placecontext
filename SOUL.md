# SOUL

## Scope
This repository supports a jobs-first operating model. The implementation focus is to make jobs,
chains, schedules, events, data, artifacts, and execution across worker nodes predictable and easy
to operate.

Local AI is an infrastructure capability for clustering and embeddings. It does not expose an
agent-management or chat workspace in the web application.

## Non-goals

- Avoid changing job orchestration behavior without first ensuring backward compatibility.
- Keep tenant boundaries, permissions, and durable run history explicit.

## Operating expectations

- Keep job creation, execution, and inspection focused and understandable.
- Keep worker joining and local-AI clustering operational without coupling them to a chat UI.
- Keep side panels fully opaque and readable.
