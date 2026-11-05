# Real estate listing platform workflow

Import `workflow.json` into Settings → Backup to create **Real estate · Build and verify**.
The import creates a dedicated project and three jobs; it does not run the workflow or deploy an app.
Generate it with `python3 examples/real-estate-sdlc/build.py --output /tmp/real-estate-workflow.json`.

The workflow prepares a verification attempt, assigns requirements and implementation as Agent tasks,
packages the local source files as an artifact, requests an independent agent review, tests the preview
in QEMU, and calculates production readiness. If the gate fails, an agent prepares remediation and
Repeat flow reruns the preceding stages, carrying the prior findings. Three total attempts are allowed.
The workflow fails if the final attempt does not meet the gate. Infrastructure and execution errors
stop the run; failed quality assertions should be reported as evidence so they reach the repeat gate.

Agent tasks and external Agent in the loop steps wait for a connected agent to submit a response.
For automatic execution, choose Automatic and an enabled model connection on each agent step,
or generate the package with `--connection-id <saved-connection-id> --model <configured-model>`.
Automatic agents use the platform's permission-checked sandbox job tools and save a JSON result
directly into the pipeline. Each step has a 30-minute and 20-model-turn limit.
Click a step in the workflow run canvas to open its movable execution modal. The Agent activity panel shows model waits, public progress updates, tool timings,
failures and live job-log links as soon as the agent submits a job. Codex public messages appear
as they arrive; other providers report updates when their response returns. Select View logs to
follow stdout/stderr in the modal’s Job logs. Activity refreshes while the run is open and is retained
with its encrypted step history (the latest 400 events per step, including repeat attempts).
Earlier runs without activity records cannot be reconstructed. Final JSON stays in Pipeline output.
External handoffs still need an agent with its own coding/testing tools.
The implementation response is JSON text containing `files: [{path, content}]`, `preview_url` and
`change_summary`. An existing local project can supply these files; no repository URL is required.
Do not include secrets. This starter workflow accepts up to 4 MiB of text source and 1,000 files.

Use the configured `browser-linux` QEMU profile, guest networking and browser-agent model.
Provide an isolated preview accessible from that guest. Automatic sandbox jobs cannot start a persistent host preview server;
unavailable preview access must be reported as a blocker. No deployment is automatic.
The UI test must return the exact JSON requested in its prompt; absent or truncated reports cannot pass.

Readiness is a deterministic 0–100 weighted score: requirements 10, functionality 20, automated tests 15,
security 15, data integrity 10, accessibility 10, performance 5, operations 10 and QEMU UI 5.
At least 90 points, no blockers, and passing functionality/tests/security/data/UI gates are required.
Reviews and browser reports must match the current verification ID and source archive hash.
Evidence comes from the configured reviewer and QEMU: this is an assessment, not independent certification.
The evidence URLs and paths must be inspected by the reviewer; the scorer does not fetch them.

The source ZIP, requirements JSON and readiness report are saved through the existing Raw bundle
artifact action to private object storage. The Artifacts page links each file to its producing run.
A storage outage follows the platform's existing best-effort artifact behavior; verify the artifact links
before treating a handoff as delivered. The workflow package itself can also be saved as an artifact.

Run the local checks with:
`python3 -m unittest discover -s examples/real-estate-sdlc -p 'test_*.py'`.

Scoring checkpoints use the platform’s configured verification service (Jev):

- Requirements: coverage 40%, testability 40%, operations 20%; stops before implementation if blocked.
- Implementation review: functionality 30%, security 30%, tests 25%, data integrity 15%; a blocked result skips QEMU and reaches remediation.
- Both require a weighted score of 90/100, every criterion at least 80/100, and confidence at least 0.8 for every criterion. These are initial thresholds to calibrate against known examples, not a production guarantee.
- Final readiness also requires both scoring gates, current source/attempt identity, independent evidence and QEMU checks. A weighted average cannot override a required failure. The loop remains bounded to three attempts.

Each scoring result records its rubric, weights, model, confidence, evidence hash and blockers in pipeline output and the readiness artifact. Provider failures or missing evidence stop execution. Evidence fields are limited to 65,536 characters and are never truncated.

The reusable **Query data** canvas step reads this project’s tables using read-only SQL. Set an output field such as `listings`; the next Python job can read `json.load(sys.stdin)["listings"]["rows"]`. Rows are objects with string/null cells, accompanied by `columns`, `row_count` and `truncated: false`. Use an explicit `LIMIT` (up to the reader’s 500-row limit). Truncated queries or results over 1 MiB stop the workflow. Existing pipeline fields are retained; the selected output field is replaced. Data-read permission is required.
