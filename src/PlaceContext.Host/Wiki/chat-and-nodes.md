# Chat between nodes

*Press `[t]` in the TUI to open a secure, private chat straight between two of your PlaceContext machines — no server, no account, and both sides have to say yes.*

## What it's for

Every machine running the PlaceContext TUI can chat directly with another one. It's built for the
fleet case: you're at the master, a teammate (or you, on another box) is at a worker, and you want
a quick, secure line between machines **you own**. There's no chat server in the middle, no
account to create, and nothing is relayed anywhere — the two machines talk straight to each other,
end-to-end encrypted.

## Before you start

Both machines need the PlaceContext TUI running — a machine only answers a network scan and
accepts calls while its TUI is open. To chat on the same local network, that's all you need. To
reach a machine somewhere else, you'll want its address and its id (both shown on its chat
screen), or the two machines on the same private mesh network (see *Cluster and nodes*).

## Start a chat

1. Run `pctl tui` and press **`[t]`**. The header shows *your* machine's id and the port it's
   listening on.
2. It **scans your network** for other PlaceContext machines and lists what it finds.
3. Press `tab` to select a machine, then press **`⏎`** (with the input empty) to call it. To
   reach a machine that isn't on your local network, type its address and id instead. `ctrl+r`
   rescans.
4. **Both sides have to agree.** The other operator sees a prompt — *"… wants to chat — [y]
   accept, [n] decline"* — and nothing is read from the connection until they accept. While a
   chat or a pending request is open, other callers are politely told you're busy.
5. Once connected, type and press `⏎` to send. `ctrl+d` hangs up; `esc` leaves the view but keeps
   the conversation alive in the background — a flash tells you when a new message arrives.

If you're somewhere else in the TUI when a call or message comes in, you'll see a one-line flash
("chat request from … — press [t]"), so you never miss someone reaching out while you're watching
the dashboard.

## Key reference

| Key | When | What it does |
|---|---|---|
| `tab` | Machines found | Select the next one |
| `⏎` (empty input) | Not connected | Call the selected machine |
| `⏎` (with text) | Not connected | Call a machine you typed by address and id |
| `⏎` (with text) | Connected | Send the message |
| `ctrl+r` | Not connected | Rescan the network |
| `y` / `n` | Incoming call | Accept / decline |
| `ctrl+d` | Connected | Hang up |
| `esc` | Any time | Back to the dashboard — the chat stays open |

## A session, end to end

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

Each line is timestamped and labelled with who sent it. Your own lines are marked `you`; the
other machine's are labelled with its id.

## Why it's secure

You don't have to configure any of this — it's simply how the chat works:

- **Everything is encrypted** between the two machines, end to end. Nobody in between can read it.
- **Each machine proves who it is.** A call only connects if the machine at the other end really
  is the one you meant to reach — an impostor advertising someone else's name simply can't
  complete the connection.
- **Messages can't be forged.** A line's author is whoever the secure connection proved them to
  be, not a name typed into a message.
- **You're always asked first.** An incoming call is shown as a request and nothing is read until
  you accept; declining just closes it.

Each machine keeps a stable identity so its id stays the same across restarts. If you ever want a
machine to become a brand-new identity, an operator can reset its saved key.

## Chatting across networks

Machines on the same local network find each other automatically. For machines in different
places, call one directly by its address and id — for example over a private mesh network (see
`pctl mesh` and the `--vpn-*` options in *Cluster and nodes*):

```
100.64.0.7:7443 4Q7Z…32-CHARACTER-NODE-ID…
```

## When to reach for it

It's the quickest way to coordinate a fleet action without leaving the console. A common
pattern: you finish a deploy on the master, press `[t]`, and tell the operator at a worker to
pull the update — all from inside the same dashboard where you can both see what's running. No
external chat tool, no copy-pasting between windows.

Because it's tied to the machines themselves rather than to accounts, it's also a handy sanity
check: if you can reach a machine here and see its id, you know it's up, on the network, and
really the box you think it is.

## Scope

This is for **operators of machines they own** — a few boxes talking to each other. It isn't a
multi-tenant messaging system: keeping different customers' networks apart is the mesh's job
(`pctl mesh tenant add`), not this chat.

## Troubleshooting

- **"no other nodes found"** — the other machine needs its TUI running too, and both must be on
  the same local network. Across networks, call it directly by address.
- **"identity mismatch"** — the machine at that address isn't the one you expected. Re-check its
  id on its chat screen; if it legitimately reset its key, its id changed.
- **"address in use"** — another program (or a second TUI) is already using the chat port. Set
  `PCTL_CHAT_PORT` and call with the explicit port.
- **The caller sees nothing happen** — the other side hasn't answered the accept/decline prompt
  yet. Nothing flows until they do.
- **"busy"** — the machine you called is already in a chat or has another request pending. Wait
  and try again.
- **A message came in but you missed it** — leaving the chat view with `esc` keeps the
  conversation alive in the background; press `[t]` again to return to it. Only `ctrl+d` actually
  hangs up.
- **Wrong machine answered** — double-check the id shown on the peer's chat screen against the one
  you're calling; the id is what pins the identity, not the address.
