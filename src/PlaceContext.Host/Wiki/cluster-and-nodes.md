# Cluster and nodes

*Start on one machine, then add more computers with a single join code so jobs and projects spread across your fleet.*

## The model: a master plus workers

PlaceContext runs across one or more computers. One is the **master** — where the portal lives
and where you manage things. The others are **workers** that add capacity: as you add them, new
jobs and work simply start running on them too.

You can run everything on one machine to start, and add computers whenever you need more room.

## Start on one machine

```bash
pctl dev up                 # set everything up on this machine
pctl dev up --rebuild       # rebuild first, if you're on a source checkout
pctl dev down               # tear it down
```

Two commands keep a single machine healthy day to day:

```bash
pctl ensure        # start it back up (and repair common breakage) — safe to run any time
pctl autostart     # have it start automatically at boot
```

`ensure` also fixes common damage — like a Docker cleanup that removed something it needs — and
tells you plainly on the rare occasion a full recreate is the only fix.

You can tweak a few things with environment variables when you bring it up: `PCTL_PORT` (the
portal's port, default `7700`) and `PCTL_AGENTS` (how many workers to start locally).

## Add a worker computer

This is the whole point of the fleet. Two steps: get a code from the master, use it on the new
machine.

### 1. On the master, get a join code

```bash
# Linux k3s master:
sudo placecontext join-code
# Mac / Docker (k3d) master — same command; token comes from the k3d server container:
placecontext join-code
```

It's a single string (`PC2.…`) that carries everything the new machine needs. When the master is
on Tailscale, the code embeds the mesh address so it keeps working behind NAT — including **Mac
laptop master → remote Linux worker**.

Mac/docker masters need the k3d API published on `:6443` (new installs do this). If join-code
says the API is localhost-only, recreate once:

```bash
k3d cluster delete placecontext && placecontext install --docker
```

### 2. On the new computer (Linux), use it

Workers install k3s and must be Linux. Either way works:

- **In the TUI** — run `placecontext`, choose **Connect**, paste the code, press `⏎`.
- **From a shell** — `sudo placecontext connect --code 'PC2.…'`

Either way, the new machine joins and starts taking on work right away.

## The TUI dashboard

`pctl tui` is your operator console. With a cluster up, it shows a spinning globe on the left and,
on the right, live tables of your **machines**, what's **running**, and your **jobs** — refreshed
every second or two. Anything that needs attention (a machine not ready, something crashing, the
database down, an out-of-date database) surfaces at the top, in red when it's urgent.

| Key | What it does |
|---|---|
| `↑↓` / `jk` | Move the selection across machines, workloads, and jobs |
| `⏎` | Open logs, or a job's run history and run detail |
| `R` | Run the selected job |
| `s` | A job's settings: network access, extra outputs, time limit |
| `x` | Stop the selected job (with confirmation) |
| `/` | Search what's known about your projects |
| `g` | Live CPU and memory graphs |
| `m` | Watch AI agent activity |
| `p` | Open the portal, signed in |
| `$` | Manage the subscription |
| `a` | Add a worker on this machine |
| `u` | Update to the latest and redeploy |
| `t` | Secure chat with your other machines |
| `c` / `r` / `q` | Cycle theme / refresh / quit |

Before you've set anything up, the welcome screen offers **`[u]`** to set up on this machine and
**`[j]`** to join one you already run.

## Check on things

```bash
pctl status            # your machines and everything running
pctl logs -f           # watch the logs live
pctl url               # print the portal address
pctl doctor            # confirm the machine has what it needs
pctl doctor --go-live  # the full readiness check against a running cluster
```

`pctl doctor --go-live` is the thorough one: it confirms the cluster is reachable, the database is
up to date, the app is ready, and even runs a real test job in each language to prove jobs work.

## Update to the latest

```bash
pctl update             # get the latest source
pctl update --deploy    # get the latest and roll it out
pctl deploy             # roll out what you already have
```

The TUI's **`[u]`** is the one-key "get current and running" — it does `pctl update --deploy` for
you.

## Stopping things

```bash
pctl kill job <name>    # remove a job and its whole run history
pctl kill pod <name>    # restart a workload (it comes back automatically)
pctl kill node <name>   # remove a worker machine from the fleet
```

Each asks for confirmation unless you pass `--yes`. The TUI's `[x]` does the same, with its own
confirmation.

## Backups and resilience

```bash
pctl db backup-now      # take a full backup right now
pctl db backups         # list the backups you're holding (a nightly one runs automatically)
pctl db restore         # restore from the latest backup (or a specific one)
pctl db ha              # run the database with live replicas for resilience
```

A backup runs automatically every night and is kept for a week. `pctl db restore` replaces the
current data, so it asks you to type `yes` to confirm unless you pass `--yes`.

## Spanning networks

To run workers in different locations, you can put the fleet on a private mesh network so the
machines can reach each other securely wherever they are. Bring the master up with the mesh
options for your setup, and joined workers come along automatically — see `pctl mesh` and the
`--vpn-*` options.
