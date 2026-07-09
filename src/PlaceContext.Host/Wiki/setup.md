# Setting up PlaceContext

*From a fresh machine to a working platform: the guided wizard, dev vs fleet, Tailscale, and the feature keys.*

## The one command

```bash
./deploy/pctl setup
```

The wizard walks every step in order and each one maps to a plain `pctl` command, so nothing is
magic. The long-form version of this journey lives in the repo at `docs/SETUP.md`.

1. **Doctor** — checks Docker, k3d, kubectl (and tells you how to get what's missing).
2. **Mode** — what this machine should be:
   - **dev** — everything on this machine (a real 1-server + 2-agent k3d cluster).
   - **server** — production master of a new fleet.
   - **join** — a worker joining an existing fleet with a join code.
3. **Tailscale** — fleet nodes connect via Tailscale. Paste an OAuth client once
   (`pctl ts-oauth`); every join afterwards mints its own single-use tailnet key —
   you never handle keys again.
4. **Platform keys** — generate the event-ingest key and the inbound-SMS key (or skip).
   They're kept in a local overlay that every deploy re-applies, so redeploys never
   silently disable the gateways.
5. **Bring-up** — `dev up`, `server up`, or `join --code …` for the chosen mode.

## Adding fleet machines

On the master: `sudo pctl join-code` prints one string (a `PC2.` code) carrying the master's
tailnet address, the cluster token, and a fresh Tailscale key. On the new machine:
`sudo pctl join --code '…'` — or press `[j]` in the TUI. One string, on the tailnet, in the
cluster. Codes stay valid for an hour; the node itself is a durable tailnet device.

## What the feature keys unlock

| Key | Feature |
|---|---|
| Ingest key | External systems `POST /ingest/{event}` to fire event triggers |
| SMS inbound key | Your SMS provider webhooks to `POST /sms/inbound` — messages stored encrypted, `sms.received` fires triggers |
| GitHub OAuth app | Import repositories from the Import page |

## After setup

- **First project**: portal → Onboarding, or import from GitHub / an Obsidian vault (`/import` —
  notes and `[[wikilinks]]` become the project's graph).
- **Agents**: connect MCP at `http://<host>:7700/mcp`; the `onboard` tool bootstraps a repo, and
  `setup_hermes` installs the job-orchestration skill into the project.
- **Dashboard**: `pctl tui` — the live cluster view.
