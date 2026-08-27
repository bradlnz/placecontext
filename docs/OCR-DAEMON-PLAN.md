# OCR Daemon Pipeline — Implementation Plan

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│  Mac (worker@100.64.0.10) — Apple Silicon                       │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Python OCR Daemon (launchd background service)           │  │
│  │  ┌─────────────────┐    ┌──────────────────────────────┐  │  │
│  │  │ Poll Loop        │───>│ Unlimited-OCR MLX (mlx-vlm) │  │  │
│  │  │ (every 30s)      │    │ LoJexLLM/Unlimited-OCR-MLX  │  │  │
│  │  └──────┬──────────┘    └──────────────┬───────────────┘  │  │
│  │         │                              │                  │  │
│  └─────────┼──────────────────────────────┼──────────────────┘  │
└────────────┼──────────────────────────────┼─────────────────────┘
             │ GET /api/ocr/pending         │ POST /api/ocr/complete
             │ (Bearer token)               │ (markdown result)
             ▼                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  PlaceContext Host (.NET 10)                                    │
│  ┌──────────────┐  ┌──────────────────┐  ┌──────────────────┐  │
│  │ OcrController │  │ job_run_artifacts │  │ proj_NNN/        │  │
│  │ (new)         │  │ +OcrStatus col   │  │ ocr_results      │  │
│  │               │  │ (new migration)  │  │ (project schema) │  │
│  └──────────────┘  └──────────────────┘  └──────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

## Phase 1: Backend — Tracking & API (C# / .NET)

### 1a. Add `OcrStatus` to `RunArtifactLinkRow`

**File:** `src/PlaceContext.Infrastructure/Persistence/RunArtifactLinkRow.cs`

Add nullable `DateTimeOffset? OcrProcessedAt` and `string? OcrError` columns to track OCR state. No enum needed — null means unprocessed, non-null timestamp means processed, `OcrError` non-null means failed.

### 1b. EF Migration

**New migration** via `dotnet ef migrations add AddOcrTracking`:

```sql
ALTER TABLE job_run_artifacts ADD COLUMN ocr_processed_at timestamptz NULL;
ALTER TABLE job_run_artifacts ADD COLUMN ocr_error text NULL;
CREATE INDEX ix_job_run_artifacts_ocr ON job_run_artifacts (ocr_processed_at)
    WHERE ocr_processed_at IS NULL;
```

The partial index keeps the "pending" query fast as artifact count grows.

### 1c. Domain Entity Update

**File:** `src/PlaceContext.Domain/Entities/RunArtifactLink.cs`

Add `OcrProcessedAt` and `OcrError` properties. Update `Rehydrate` factory method.

### 1d. Repository — Pending Artifacts Query

**File:** `src/PlaceContext.Infrastructure/Persistence/EfRunArtifactLinkRepository.cs`

New method: `ListPendingOcrAsync(int take)` — returns artifacts where `OcrProcessedAt IS NULL` and `ContentType` starts with `image/`, `application/pdf`, or `text/` (all artifact types per user's request, filtered to processable ones). Ordered by `CreatedAt` ascending.

### 1e. Repository Interface Update

**File:** `src/PlaceContext.Domain/Repositories/IRunArtifactLinkRepository.cs`

Add `ListPendingOcrAsync` and `MarkOcrProcessedAsync(Guid artifactId, string? error)`.

### 1f. New Controller: `OcrController`

**File:** `src/PlaceContext.Host/Controllers/OcrController.cs`

```
[Authorize]
public sealed class OcrController : ControllerBase
{
    // GET /api/ocr/pending?take=10
    // Returns artifacts needing OCR with download URLs (presigned or direct).
    // Filters: OcrProcessedAt IS NULL, content-type is processable.
    // Auth: Bearer token (existing API token system).

    // POST /api/ocr/complete
    // Body: { artifactId, markdown, error? }
    // On success: stores markdown in project's ocr_results table,
    //             marks artifact as processed.
    // On error: marks artifact as processed with error message.
}
```

### 1g. OCR Result Storage Service

**File:** `src/PlaceContext.Application/Ocr/OcrResultStorageService.cs`

New service that uses `IProjectDataStore.AppendReadOnlyRowsAsync` to write OCR results to a `ocr_results` table in the project's schema:

```sql
-- Auto-created on first OCR result per project:
CREATE TABLE proj_xxx.ocr_results (
    ingested_at  timestamptz NOT NULL,
    artifact_id  uuid NOT NULL,
    run_id       uuid NOT NULL,
    job_id       uuid NOT NULL,
    title        text,
    content_type text,
    markdown     text NOT NULL
);
```

Uses the same provenance pattern as `DataMappingIngestionService` (provenance columns + read-only system table).

### 1h. PlaceContextService Integration

**File:** `src/PlaceContext.Application/Services/PlaceContextService.cs`

Add `CompleteOcrAsync(Guid artifactId, string markdown, string? error)` method that orchestrates: store result → mark artifact processed.

### 1i. DI Registration

**File:** `src/PlaceContext.Infrastructure/DependencyInjection.cs`

Register `OcrResultStorageService` as singleton.

### 1j. Auth Configuration

The existing `BearerTokenHandler` already validates API tokens. The OCR daemon will use a standard user API token. No new auth scheme needed — just ensure the token has read access to artifacts and write access to project data.

---

## Phase 2: Python Daemon

### 2a. Project Structure

```
deploy/ocr-daemon/
├── setup.sh              # One-shot setup (similar to release/local-ai installation)
├── config.yaml           # Daemon configuration
├── ocr_daemon.py         # Main daemon script
├── placecontext_client.py # API client for polling/results
└── com.placecontext.ocr.plist  # launchd plist for background service
```

### 2b. `setup.sh`

Following the worker-service pattern in `deploy/release/local-ai`:

```bash
#!/usr/bin/env bash
# One-shot setup: install MLX deps, download Unlimited-OCR-MLX, start daemon.
#
# Usage:
#   bash setup.sh                    # full setup + start
#   bash setup.sh --daemon-only      # skip model download, just start

set -euo pipefail

VENV_DIR="$HOME/.venv/ocr-daemon"
MODEL="LoJexLLM/Unlimited-OCR-MLX"
PORT="${OCR_PORT:-9000}"

# 1. Create venv
# 2. Install: mlx-vlm, requests, pyyaml, pymupdf (for PDF→image conversion)
# 3. Pre-download model via huggingface_hub
# 4. Install launchd plist
# 5. Start service
```

### 2c. `config.yaml`

```yaml
placecontext:
  base_url: "https://your-placecontext-host"  # or Tailscale IP
  api_token: "pcat_xxx..."                     # user API token

model:
  name: "LoJexLLM/Unlimited-OCR-MLX"
  # "gundam" mode for single images, "base" for multi-page/PDF
  image_mode: "gundam"
  base_size: 1024
  image_size: 640

daemon:
  poll_interval_seconds: 30
  batch_size: 5
  max_concurrent: 2       # MLX inference concurrency
  temp_dir: "/tmp/ocr-daemon"
```

### 2d. `ocr_daemon.py`

Main daemon loop:

```python
import time, tempfile, os, json
from mlx_vlm import load, generate
from mlx_vlm.prompt_utils import apply_chat_template
from mlx_vlm.utils import load_config
import fitz  # PyMuPDF for PDF→images
from placecontext_client import PlaceContextClient

class OCRDaemon:
    def __init__(self, config):
        self.client = PlaceContextClient(
            config["placecontext"]["base_url"],
            config["placecontext"]["api_token"]
        )
        self.model, self.processor = load(config["model"]["name"])
        self.config = config["model"]

    def run(self):
        while True:
            pending = self.client.get_pending_artifacts(
                take=self.config.get("batch_size", 5)
            )
            for artifact in pending:
                self.process_artifact(artifact)
            time.sleep(self.config.get("poll_interval_seconds", 30))

    def process_artifact(self, artifact):
        try:
            # 1. Download artifact bytes from PlaceContext
            content = self.client.download_artifact(
                artifact["runId"], artifact["id"]
            )

            # 2. Convert to images if PDF
            if artifact["contentType"] == "application/pdf":
                images = self.pdf_to_images(content)
            else:
                images = [content]  # already an image

            # 3. Run OCR on each image
            markdown_parts = []
            for img_bytes in images:
                # Save to temp file, run MLX inference
                with tempfile.NamedTemporaryFile(suffix=".png") as f:
                    f.write(img_bytes)
                    f.flush()
                    result = self.run_ocr(f.path)
                    markdown_parts.append(result)

            markdown = "\n\n---\n\n".join(markdown_parts)

            # 4. Post result back
            self.client.complete_ocr(artifact["id"], markdown=markdown)

        except Exception as e:
            self.client.complete_ocr(artifact["id"], error=str(e))

    def run_ocr(self, image_path):
        prompt = apply_chat_template(
            self.processor,
            load_config(self.config["name"]),
            "document parsing.",
            num_images=1
        )
        output = generate(
            self.model, self.processor, prompt,
            [image_path], verbose=False,
            temp=0.0, max_tokens=32768
        )
        return output

    def pdf_to_images(self, pdf_bytes, dpi=300):
        # Use PyMuPDF to convert PDF pages to images
        doc = fitz.open(stream=pdf_bytes, filetype="pdf")
        mat = fitz.Matrix(dpi / 72, dpi / 72)
        images = []
        for page in doc:
            pix = page.get_pixmap(matrix=mat)
            images.append(pix.tobytes("png"))
        doc.close()
        return images
```

### 2e. `placecontext_client.py`

```python
import requests

class PlaceContextClient:
    def __init__(self, base_url, api_token):
        self.base_url = base_url.rstrip("/")
        self.headers = {"Authorization": f"Bearer {api_token}"}

    def get_pending_artifacts(self, take=5):
        r = requests.get(
            f"{self.base_url}/api/ocr/pending",
            params={"take": take},
            headers=self.headers, timeout=10
        )
        r.raise_for_status()
        return r.json()

    def download_artifact(self, run_id, artifact_id):
        r = requests.get(
            f"{self.base_url}/runs/{run_id}/artifacts/{artifact_id}",
            headers=self.headers, timeout=60
        )
        r.raise_for_status()
        return r.content

    def complete_ocr(self, artifact_id, markdown=None, error=None):
        r = requests.post(
            f"{self.base_url}/api/ocr/complete",
            json={"artifactId": artifact_id, "markdown": markdown, "error": error},
            headers=self.headers, timeout=10
        )
        r.raise_for_status()
```

### 2f. `com.placecontext.ocr.plist`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "...">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.placecontext.ocr</string>
    <key>ProgramArguments</key>
    <array>
        <string>/Users/jarvis/.venv/ocr-daemon/bin/python</string>
        <string>/Users/jarvis/ocr-daemon/ocr_daemon.py</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>/Users/jarvis/logs/ocr-daemon.log</string>
    <key>StandardErrorPath</key>
    <string>/Users/jarvis/logs/ocr-daemon.err</string>
</dict>
</plist>
```

---

## Phase 3: Deployment

### 3a. Remote Setup Script

**File:** `deploy/ocr-daemon/remote-setup.sh`

```bash
#!/usr/bin/env bash
# SSH into the Mac and run setup remotely.
#
# Usage:
#   bash remote-setup.sh [token]

set -euo pipefail

MAC_HOST="worker@100.64.0.10"
SSH_KEY="$HOME/.ssh/id_ed25519"
TOKEN="${1:-}"

echo "=== Setting up OCR daemon on $MAC_HOST ==="

# Upload the daemon files
scp -i "$SSH_KEY" -r deploy/ocr-daemon/ "$MAC_HOST:~/ocr-daemon/"

# Run setup remotely
ssh -i "$SSH_KEY" "$MAC_HOST" "cd ~/ocr-daemon && bash setup.sh --token '$TOKEN'"

echo "=== OCR daemon installed ==="
echo "View logs: ssh -i $SSH_KEY $MAC_HOST 'tail -f ~/logs/ocr-daemon.log'"
```

---

## Phase 4: Portal Integration (Optional, Lower Priority)

### 4a. Artifact OCR Status Badge

Show OCR status on artifact cards in the portal:
- Pending: clock icon
- Processing: spinner
- Completed: checkmark (click to view markdown)
- Failed: error icon with tooltip

### 4b. OCR Results Tab

Add a tab to the project data view showing `ocr_results` table contents with full-text search.

---

## Files to Create/Modify

| File | Action |
|------|--------|
| `src/PlaceContext.Infrastructure/Persistence/RunArtifactLinkRow.cs` | Add `OcrProcessedAt`, `OcrError` |
| `src/PlaceContext.Domain/Entities/RunArtifactLink.cs` | Add OCR properties |
| `src/PlaceContext.Domain/Repositories/IRunArtifactLinkRepository.cs` | Add pending/completed methods |
| `src/PlaceContext.Infrastructure/Persistence/EfRunArtifactLinkRepository.cs` | Implement pending/completed queries |
| `src/PlaceContext.Infrastructure/Persistence/AppDbContext.cs` | Configure new columns + index |
| `src/PlaceContext.Infrastructure/Migrations/` | New migration |
| `src/PlaceContext.Host/Controllers/OcrController.cs` | **New** — API endpoints |
| `src/PlaceContext.Application/Ocr/OcrResultStorageService.cs` | **New** — project table storage |
| `src/PlaceContext.Application/Services/PlaceContextService.cs` | Add `CompleteOcrAsync` |
| `src/PlaceContext.Infrastructure/DependencyInjection.cs` | Register new service |
| `deploy/ocr-daemon/setup.sh` | **New** — Mac setup |
| `deploy/ocr-daemon/config.yaml` | **New** — daemon config |
| `deploy/ocr-daemon/ocr_daemon.py` | **New** — main daemon |
| `deploy/ocr-daemon/placecontext_client.py` | **New** — API client |
| `deploy/ocr-daemon/com.placecontext.ocr.plist` | **New** — launchd service |
| `deploy/ocr-daemon/remote-setup.sh` | **New** — SSH deploy script |

---

## Key Design Decisions

1. **Tracking via nullable timestamp** on `job_run_artifacts` — no new table, leverages existing partial index for fast pending queries
2. **Project-scoped `ocr_results` table** via `IProjectDataStore` — same pattern as `DataMappingIngestionService`, read-only system table with provenance columns
3. **MLX Unlimited-OCR** (`LoJexLLM/Unlimited-OCR-MLX`) — ported version, ~18 tok/s on M4 Pro, MIT license
4. **launchd background service** — native macOS daemon management, auto-restart on crash
5. **Bearer token auth** — reuses existing `user_api_tokens` system, no new auth scheme
6. **Presigned download URLs** for artifacts — avoids the daemon needing full MinIO access

## MLX Model Options

| Model | Size | Notes |
|-------|------|-------|
| `LoJexLLM/Unlimited-OCR-MLX` | ~6.2 GB (FP16) | Full model, best accuracy |
| `LoJexLLM/Unlimited-OCR-MLX-fixed` | ~6.2 GB | Bugfixes for RMSNorm and other issues |
| `sahilchachra/unlimited-ocr-8bit-mlx` | ~3 GB | 8-bit quantized, fits 8 GB Macs |

Recommended: Start with `LoJexLLM/Unlimited-OCR-MLX-fixed` (bugfixes), fall back to 8-bit if memory constrained.

## Performance Estimates (M4 Pro)

- Vision encoding: ~0.5s per image
- Text generation: ~18 tok/s
- Single A4 page: ~2.0s
- 10-page PDF: ~15s
- Batch of 5 artifacts: ~30-60s depending on complexity
