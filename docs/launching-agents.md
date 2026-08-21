# Launching agents with minted Tailscale keys

*Add a worker to the fleet from the portal — PlaceContext mints a fresh, short-lived Tailscale
auth key from OAuth credentials in the vault and hands you a ready-to-run join code. No pasting
long-lived keys, no manual key rotation.*

## What this changes

Previously, adding a Tailscale-joined worker meant minting an auth key by hand in the Tailscale
admin console and passing it into `placecontext join-code --ts-authkey …`. That key was long-lived
and shared.

Now the master can mint keys **on demand**:

- Store your Tailscale **OAuth trust credentials** (`TS_CLIENT_ID` + `TS_CLIENT_SECRET`) once, in
  the vault.
- On the **Cluster** page, **Launch agent** mints a fresh key via the Tailscale API and produces a
  `PC2.…` join code with the key embedded.
- Run the printed `placecontext connect --code …` on the new machine. Its k3s agent joins the
  tailnet and the cluster in one step, using the packaged app image (`placecontext.tar`) so jobs
  can schedule on it immediately.

Each key is **ephemeral**, **pre-authorized**, single-use (`reusable: false`), **tagged**, and
expires in ~10 minutes — enough to join, then it's gone. Ephemeral nodes are auto-removed from the
tailnet shortly after they go offline, so churned workers don't pile up as stale devices.

## One-time setup

### 1. Create a Tailscale OAuth client

In the Tailscale admin console → **Settings → OAuth clients**, create a client with the
**`auth_keys`** write scope and assign it the tag you'll use for agents (e.g. `tag:agent`). You'll
get a **client ID** and **client secret**.

> Keys minted through an OAuth client **must** be tagged, and the client must own that tag. Define
> the tag in your ACL policy first, e.g.:
>
> ```jsonc
> "tagOwners": { "tag:agent": ["autogroup:admin"] }
> ```

OAuth clients don't expire (unlike personal access tokens), so this is set-and-forget.

### 2. Store the credentials in the vault

Add these two secrets to the vault under the cluster system project (the **Cluster** page's
credentials form writes here):

| Name               | Value                          |
| ------------------ | ------------------------------ |
| `TS_CLIENT_ID`     | the OAuth client ID            |
| `TS_CLIENT_SECRET` | the OAuth client secret        |
| `TS_TAG`           | *(optional)* tag, default `tag:agent` |

Values are encrypted at rest with the app's Data Protection key ring; plaintext never leaves the
Host and is only used to call the Tailscale API when minting a key.

## Launching an agent

1. Open **/cluster** (requires `settings.manage`).
2. Click **Launch agent**. The Host mints a fresh key and shows a `PC2.…` join code plus a ready
   command.
3. On the new machine:

   ```bash
   curl -fsSL https://get.placecontext.io/install.sh | bash
   sudo placecontext connect --code PC2.…      # the key is embedded in the code
   ```

That's it — the node joins the tailnet and the k3s fleet, imports the app image, and starts taking
jobs.

If the credentials aren't in the vault yet, **Launch agent** tells you exactly which secrets to add
instead of minting.

## CLI: pass a key and code into upgrade / connect

The same material works from the CLI, so you can upgrade an existing machine and (re)join it in one
step:

```bash
# Join code embeds the key (PC2, from the portal or `join-code --ts-authkey`):
sudo placecontext connect --code PC2.…

# Or supply the key separately (keyless PC1 code, or a rotating key):
placecontext connect --code PC1.… --ts-authkey tskey-auth-…

# Refresh the CLI and (re)join a worker in one shot:
sudo placecontext upgrade --code PC2.… [--ts-authkey tskey-auth-…]
```

`--ts-authkey` supplied on the command line overrides any key embedded in the code.

## How the key is minted

The minting happens server-side, inside the request scope that already carries the tenant context
(so the tenant-scoped vault reads work):

1. `LaunchClusterAgentCommand` reads `TS_CLIENT_ID` / `TS_CLIENT_SECRET` from the vault and
   decrypts them.
2. `POST https://api.tailscale.com/api/v2/oauth/token` (client-credentials grant) → access token.
3. `POST https://api.tailscale.com/api/v2/tailnet/-/keys` with:

   ```json
   {
     "capabilities": { "devices": { "create": {
       "reusable": false, "ephemeral": true, "preauthorized": true,
       "tags": ["tag:agent"]
     }}},
     "expirySeconds": 600,
     "description": "placecontext agent join"
   }
   ```

4. The returned `tskey-auth-…` is embedded in the `PC2` join code the master already builds for the
   fleet's mesh address.

## Troubleshooting

- **"add TS_CLIENT_ID and TS_CLIENT_SECRET to the vault"** — the credentials aren't stored (or are
  under the wrong project). Add them via the Cluster page.
- **Key minted but the node won't authorize** — check the tag is owned by the OAuth client in your
  ACL policy, and that `preauthorized` is allowed for that tag.
- **Node joins but no jobs schedule** — the app image import lagged; see
  [`cluster-and-nodes`](../src/PlaceContext.Host/Wiki/cluster-and-nodes.md) and re-import
  `placecontext-local.tar`.
- **`join code host … is not a Tailscale 100.x address`** — the master isn't advertising its mesh
  IP; bring Tailscale up on the master and regenerate.

## See also

- [`cluster-and-nodes`](../src/PlaceContext.Host/Wiki/cluster-and-nodes.md) — the master/worker model
- [`deploy/README.md`](../deploy/README.md) — client install and day-2 ops
- Tailscale docs: *Using OAuth clients* and *Auth keys*
