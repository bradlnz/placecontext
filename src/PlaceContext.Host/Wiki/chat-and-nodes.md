# Chat between nodes

*Press `[t]` in the TUI for encrypted operator-to-operator chat between PlaceContext nodes — mutual TLS 1.3, self-certifying identities, and both sides must say yes.*

## What it is

Every machine running the PlaceContext TUI is a **node** with a stable cryptographic identity.
Two operators on two nodes can open a direct, encrypted chat channel between them — no server in
the middle, no account, no relay. It's built for the fleet case: you're at the master, a
colleague (or you, on another box) is at a worker, and you want a secure line between machines
**you own**.

This is the chat half of **PCSP** — the PlaceContext Sync Protocol (`docs/SYNC-PROTOCOL.md`,
§10). The Go TUI implementation interoperates byte-for-byte with the C# implementation
(`TlsChatListener` / `TlsChatClient` in `PlaceContext.Sync.Transport`).

## Using it

1. Run `pctl tui` and press **`[t]`**. The header shows *your* NodeId (32 characters) and the
   port you're listening on.
2. Opening the view **scans the LAN** for other PlaceContext nodes. Found nodes are listed as
   `NODEID  host:port`.
3. `tab` selects a node; **`⏎` on an empty input dials it**. Or dial manually by typing
   `host[:port] NODEID` (the port defaults to 7443; the NodeId is shown on the peer's chat
   screen). `ctrl+r` rescans.
4. **Both sides must approve.** The callee sees a modal — `<nodeid> wants to chat — [y] accept,
   [n] decline` — and *nothing is read from the connection until the operator accepts*. While
   either a conversation or a pending request is open, additional callers are politely refused
   with "busy".
5. Once connected: type and `⏎` to send, `ctrl+d` to hang up, `esc` to leave the view (the
   conversation stays open in the background — a flash tells you when a message arrives).

If you're elsewhere in the TUI, incoming requests and messages appear as one-line flashes
("chat request from a1b2c3d4… — press [t]").

### Key reference

| Key | When | Action |
|---|---|---|
| `tab` | Disconnected, nodes found | Select the next discovered node |
| `⏎` (empty input) | Disconnected | Dial the selected node |
| `⏎` (with text) | Disconnected | Dial a manual target: `host[:port] NODEID` |
| `⏎` (with text) | Connected | Send the message |
| `ctrl+r` | Disconnected | Rescan the LAN |
| `y` / `n` | Incoming request | Accept / decline the caller |
| `ctrl+d` | Connected | Hang up (sends an orderly `Bye`) |
| `esc` | Any | Back to the dashboard — an open conversation stays alive |

### A session, end to end

```
you are 4Q7ZK9M2…  ·  listening :7443
status: scan: 1 node(s) found — tab selects, ⏎ chats

nodes on your network:
❯ N8PT3W5A…  192.168.1.42:7443

⏎  → status: dialing 192.168.1.42:7443 (asking permission)…
     (the other operator sees: "4Q7ZK9M2… wants to chat — [y] accept, [n] decline")
     they press y
     — connected to N8PT3W5A…

14:02 you        deploy done on the master, run pctl update --deploy when ready
14:03 N8PT3W5A…  on it — worker rebuilding now
```

The transcript timestamps each line and attributes it to the TLS-authenticated peer (shown by
the first 8 characters of its NodeId); your own lines are marked `you`.

## Identity: the NodeId

Each node holds a long-lived **ECDSA P-256 keypair**, stored at
`~/.config/placecontext/node.key` (PKCS#8 PEM, mode 0600 — the same format the C# host
persists). Its identity is derived from the public key:

```
NodeId = crockford-base32( SHA-256(publicKey SPKI)[0..20] )     # 160-bit, 32 chars
```

This makes identities **self-certifying**: given a NodeId and a presented public key, either the
key hashes to the id or the peer is lying. There is no directory to consult and no CA to trust.
The TLS certificate itself is ephemeral (regenerated each start) — **the key is the identity,
never the cert**. Keep `node.key` if you want a stable NodeId across reinstalls; delete it to
become a new identity.

## The security model

| Property | How |
|---|---|
| **Encryption** | Mutual **TLS 1.3 only** — confidentiality, integrity, and forward secrecy come from the TLS channel; every chat byte is inside it |
| **Authentication** | Both endpoints present a self-signed certificate whose key *is* their identity key. The listener requires a client certificate; the dialer's handshake **fails unless the presented key hashes to exactly the expected NodeId** (identity pinning — no CA, no chain, no expiry trust) |
| **No spoofable sender** | Chat frames deliberately carry **no sender field**. A line's author is whoever the TLS handshake authenticated — attribution can't be forged inside a message |
| **Consent** | An incoming connection is surfaced as a request; the socket is not read until the operator accepts. Decline closes it |
| **Bounded input** | Frames are length-prefixed and capped at 1 MiB — hostile lengths are rejected before allocation |

### Discovery is plaintext — by design

The LAN scan is a UDP broadcast on port **7444**: the probe `PCSP-DISCOVER v1`, answered with
`PCSP-HERE <nodeid> <chatPort>`. It only advertises a (NodeId, port) pair — nothing secret.
Trust is established later, by the TLS handshake pinning that NodeId when you choose to dial.
A liar on the network can advertise any NodeId it wants; its handshake will simply fail.

## Ports and configuration

| Port | Protocol | Purpose | Override |
|---|---|---|---|
| **7443** | TCP (TLS 1.3) | The chat listener | `PCTL_CHAT_PORT` |
| **7444** | UDP | LAN discovery (probe/response) | — |

For nodes on different networks, dial manually with a reachable address — e.g. a Tailscale/
Headscale mesh IP (see `pctl mesh` and the `--vpn-*` flags in *Cluster and nodes*):

```
100.64.0.7:7443 4Q7Z…32-CHAR-NODEID…
```

## On the wire (for the curious)

A chat channel carries exactly two frame kinds from the PCSP framing
(`uvarint(len)` + `u8(kind)` + body):

| Kind | Name | Body |
|---|---|---|
| 5 | `Chat` | `string text`, `uvarint sentAtUnixMs` |
| 4 | `Bye` | `string reason` — an orderly hang-up |

Unknown kinds are ignored for forward compatibility. There is no `Hello` on a chat channel: the
peer's identity is exactly what its certificate proved. (Sync sessions — the reconciliation half
of PCSP, with `Hello`/`Push`/`Ack` and vector clocks — run on their own connections and ignore
chat; see `docs/SYNC-PROTOCOL.md` for the full protocol.)

## Scope

This feature is for **operators of machines they own end-to-end** — a few boxes talking to each
other with no control plane. It is not a multi-tenant messaging system: per-customer network
isolation is the mesh's job (`pctl mesh tenant add`), and anything needing ACLs between parties
belongs there, not here.

## Troubleshooting

- **"scan: no other nodes found"** — the other machine must have its TUI running (the responder
  answers scans while the TUI is up), and both must be on the same broadcast domain. Across
  networks, dial manually.
- **"identity mismatch: certificate is X, expected Y"** — the machine at that address is not the
  node you pinned. Re-check the NodeId on the peer's chat screen; if the peer legitimately
  regenerated its key, its NodeId changed.
- **"chat listener on :7443: …address in use"** — another process (or a second TUI) holds the
  port; set `PCTL_CHAT_PORT` and dial with the explicit port.
- **Caller sees nothing happen** — the callee hasn't answered the `[y]/[n]` prompt yet; nothing
  flows until they accept.
