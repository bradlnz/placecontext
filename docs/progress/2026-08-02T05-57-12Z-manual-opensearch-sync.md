# Progress — Manual OpenSearch collector sync

Timestamp: 2026-08-02T05:57:12Z

## Completed

- Added an administrator-only **Sync now** action to Data Search.
- Added an application command and inward-facing sync port, keeping HTTP and collector details in Infrastructure.
- Added an authenticated collector trigger client with explicit handling for queued, already-running, invalid, and unconfigured states.
- Added a tailnet-bound Python trigger service that starts `property-intelligence-ingest.service` without allowing duplicate concurrent runs.
- Extended `deploy.sh` to install the trigger service and configure its generated token as a Kubernetes secret without printing or committing the token.

## Verification

- Application handler test: 1 passed.
- Infrastructure gateway tests: 3 passed.
- Trigger HTTP tests: 3 passed, covering accepted, unauthorized, and already-running requests.
- Deployment shell scripts pass `bash -n` and ShellCheck when available.
- `PlaceContext.Host` builds with zero warnings and zero errors.

## Deployment state

- The feature is committed and ready for the requested combined deployment through `deploy.sh` after the remaining features are built.
- Production has not yet been modified by this change.
