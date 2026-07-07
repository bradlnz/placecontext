# PCSP — PlaceContext Sync Protocol (v1)

A peer-to-peer, application-layer protocol for two PlaceContext nodes to **discover each
other's state and reconcile project context directly**, with no control plane in the middle.

It replaces "everyone shares one Postgres" and "route everything through the Headscale droplet"
for the common case of *a few machines you own talking to each other*. Nodes exchange
**activity-log records** (the same what/why/author/delta stream the Host already routes every
change through) and converge to the same history.

> **Scope of v1.** This document specifies the *application protocol*: identity, framing, the
> message set, and the reconciliation state machine. It deliberately does **not** invent
> transport or cryptography — PCSP runs over a mutually-authenticated **QUIC + TLS 1.3**
> channel (or Noise-over-TCP). Confidentiality, integrity, and forward secrecy are the
> transport's job; a node's identity key *is* its TLS client-certificate key, so authentication
> and identity are the same fact. See [Transport binding](#transport-binding).

---

## 1. Design goals

1. **No control plane.** Two nodes with each other's address and identity can sync. No droplet,
   no coordinator, no CA.
2. **Self-certifying identity.** A `NodeId` is derived from the node's public key, so "who are
   you" is verifiable from the key alone — no directory to trust.
3. **Convergent.** Sync is commutative and idempotent: run it in any order, any number of times,
   both sides end with the same set of records. Reconnects are cheap (send only the delta).
4. **Transport-agnostic core.** The wire format and state machine know nothing about sockets. The
   same core drives QUIC today and could drive a relay, a file, or a test harness unchanged.
5. **Onion-first.** The core (`PlaceContext.Sync`) is a dependency-free leaf, unit-tested in full
   isolation. Transport and Host wiring sit *outside* it.

## 2. Identity

A node holds a long-lived signing keypair (Ed25519 at the transport layer). Its **NodeId** is:

```
NodeId = base32-crockford( SHA-256(publicKey)[0..20] )      # 160-bit, 32 chars, no padding
```

Self-certifying: given a `NodeId` and a presented public key, either the key hashes to the id or
the peer is lying. There is nothing else to check and no third party to ask.

## 3. Causal model

Every record originates at exactly one node and carries a **per-origin sequence number** that is
strictly monotonic at that origin. A node's knowledge of the whole system is therefore a
**vector clock**: `originNodeId → highest sequence seen from that origin`.

- **Frontier** — a node's current vector clock (what it has).
- **Delta for a peer** — every record whose `sequence` at its origin exceeds what the peer's clock
  shows for that origin. This is exactly "what you're missing".
- **Reconciliation** — last-writer-wins keyed by `(origin, sequence)`. Because that pair is unique
  and immutable, applying the same record twice is a no-op, and order never matters. The system is
  a grow-only set of records; the vector clock is its compact summary.

A `LamportClock` gives each *locally-authored* record its next sequence and keeps local time ahead
of anything seen, so causal ordering is preserved across nodes.

## 4. Framing

The transport delivers an ordered, reliable byte stream (a QUIC stream). PCSP frames it:

```
frame   := uvarint(len) payload          # len = byte length of payload
payload := u8(kind) body                 # kind selects the message; body is kind-specific
```

`uvarint` is LEB128 unsigned. Primitive encodings used by bodies:

| Type      | Encoding                                             |
|-----------|-----------------------------------------------------|
| `uvarint` | LEB128, 7 bits/byte, low group first                |
| `string`  | `uvarint(byteLen)` + UTF-8 bytes                    |
| `bytes`   | `uvarint(len)` + raw bytes                          |
| `guid`    | 16 raw bytes (big-endian)                           |
| `clock`   | `uvarint(count)` + count × (`string` id, `uvarint` seq) |

## 5. Messages

| kind | name        | direction | body                                               |
|------|-------------|-----------|----------------------------------------------------|
| 1    | `Hello`     | both      | `uvarint verMajor`, `uvarint verMinor`, `string nodeId`, `uvarint capCount` + caps(`string`), `clock frontier` |
| 2    | `Push`      | both      | `uvarint count` + count × `record`                 |
| 3    | `Ack`       | both      | `clock frontier`                                   |
| 4    | `Bye`       | both      | `string reason`                                    |

`record` := `string origin` · `uvarint sequence` · `guid projectId` · `string kind` · `string digest` · `bytes payload`

- **Hello** — first message each side sends. Advertises protocol version, self-certifying id,
  capability strings (forward-compat feature flags), and the sender's frontier.
- **Push** — a batch of records the *recipient* was missing (computed from the peer's Hello clock).
- **Ack** — the recipient's new frontier after applying a Push. Lets the sender learn convergence.
- **Bye** — orderly close with a human reason.

## 6. Session state machine

Symmetric — both peers run the same machine; there is no client/server role.

```
        Start()                 recv Hello              recv Bye / Close()
 New ───────────► HelloSent ───────────────► Established ───────────────► Closed
                     │  (versions negotiated:                    ▲
                     │   min common major.minor;                 │
                     │   incompatible ⇒ send Bye ────────────────┘
                     │   then Closed)
                     └─ on entry to Established:
                        push = store.RecordsSince(peer.frontier)
                        send Push(push)
   recv Push:  store.Apply(each); send Ack(store.Frontier())
   recv Ack:   record peer.frontier; if converged, idle (safe to Close)
```

Properties:

- **Order-independent.** Hellos may cross on the wire; whichever arrives first drives the local
  side to `Established`. Each side pushes the other's delta exactly once on entering `Established`.
- **Idempotent.** A duplicate `Push` re-applies records that are already present — a no-op by the
  `(origin, sequence)` key — and re-Acks the same frontier.
- **Convergent & terminating.** After each side has pushed its delta and the other has applied it,
  both frontiers dominate each other's Hello frontier; no side has anything left to send.

## 7. Version negotiation

Each `Hello` carries `major.minor`. Peers adopt `min(major)` then `min(minor)`. A mismatched
**major** is incompatible: the higher side sends `Bye("version: need major=X")` and closes.
Unknown message `kind`s and unknown capability strings are ignored, so a v1 node and a v1.3 node
interoperate at v1.

## 8. Transport binding

PCSP assumes a secure, ordered, reliable, bidirectional byte stream:

- **QUIC + TLS 1.3 (default).** One bidirectional stream per session. Both endpoints present a
  self-signed certificate whose key is the node's identity key; each verifies the peer's presented
  key hashes to the expected `NodeId`. Confidentiality/integrity/forward-secrecy come from TLS.
  QUIC's UDP + connection migration also give the best NAT traversal of the options.
- **Noise-over-TCP (alt).** `Noise_IK` with static keys = node identity keys, framed length-prefixed
  over TCP. Fewer moving parts, no cert plumbing; loses QUIC's multiplexing/migration.

The core never sees any of this — it consumes/produces frames. Discovery (how a node learns a
peer's address) is out of scope for v1: start with a configured `host:port` + the reachability
probe, and layer mDNS-on-LAN or a DHT in later.

## 9. What v1 is not

- **Not a transport or a cipher.** No hand-rolled crypto — that is TLS/Noise's job (see §8).
- **Not a discovery system.** Peer addresses are supplied; automatic discovery is future work.
- **Not multi-tenant ACLs.** That is the Headscale/self-hosted path's job. PCSP is for machines a
  single operator owns end-to-end.
