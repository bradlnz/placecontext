# Getting started

*Install PlaceContext on your own machine, sign in to the portal, and learn your way around.*

## What you get

PlaceContext runs on your own hardware and keeps everything there. It gives every project
a place to store its context, run your code as jobs, keep its own database, and be worked on
by an AI agent. It even runs a small AI model locally to draw charts and write summaries, so
**nothing about your projects ever leaves your machines**.

You drive it two ways:

- the **portal** — the web app where you click through projects, jobs, data, and reports;
- **`pctl`** and its full-screen console **`pctl tui`** — the command line and dashboard you
  use to run and grow the cluster.

## Install it

You need Docker on the machine. Everything else the installer handles.

### The quick way — the release tarball

```bash
tar xzf placecontext-<version>-linux-amd64.tar.gz
cd placecontext-<version>-linux-amd64
./install.sh
```

The installer checks for Docker, sets up anything it needs, and starts PlaceContext. When it
finishes you'll see:

```
PlaceContext is running:
  portal   http://localhost:7700/
  console  ./deploy/pctl tui        (dashboard, chat, join codes)
  help     ./deploy/pctl help
```

### From a source checkout

```bash
git clone <repo> && cd <repo>
./deploy/pctl dev up            # add --rebuild to build the image first
```

Want a different port or more worker capacity on this one machine?

```bash
PCTL_PORT=8800 pctl dev up      # serve the portal on a different port
PCTL_AGENTS=4 pctl dev up       # more worker capacity
```

If this machine should instead **join a cluster you already run** (as an extra worker
computer), don't install — see *Cluster and nodes* for the join-code flow.

## Check the machine is ready

Run this any time to confirm things are healthy:

```bash
pctl doctor
```

It checks the tools PlaceContext needs and, when a cluster is already running, walks the full
go-live checklist — everything present, the database up to date, and a real test job run in
each language to prove jobs work end to end.

## Sign in to the portal

The portal lives at **<http://localhost:7700/>**.

The easiest first sign-in skips passwords entirely: run `pctl tui` and press **`[p]`**. It
opens the portal already signed in. From there, invite and manage your team under
**Settings → Members**.

## Find your way around

The left-hand nav holds the platform-wide pages:

| Page | What you do there |
|---|---|
| **Overview** | See all your projects at a glance, with their risk bands |
| **Brain** | Explore what's known about a project — its decisions, context, and hot spots |
| **Activity Log** | Read the running history of every change: who, what, and why |
| **Reports** | Generate a report for a project, and see charts from recent job runs |
| **Requirements** | Set the standards agents are held to when they work for you |
| **Wiki** | This documentation |
| **MCP Inspector** | Watch, live, every action an AI agent takes |

Open any project and you also get its own pages: **Jobs**, **Data** (the
project's own database), and a **Vault** for its secrets.

## The operator console: `pctl tui`

```bash
pctl tui
```

This is your day-to-day cockpit — a live dashboard of your machines, what's running, and your
jobs, refreshed every second or two. One key does each job:

| Key | What it does |
|---|---|
| `↑↓` / `jk` | Move the selection |
| `⏎` | Open logs, or a job's run history |
| `R` | Run the selected job |
| `s` | Change a job's settings |
| `x` | Stop a job |
| `g` | Live CPU / memory graphs |
| `m` | Watch AI agent activity |
| `/` | Search what's known about your projects |
| `p` | Open the portal, already signed in |
| `a` | Add a worker computer |
| `u` | Update to the latest and redeploy |
| `t` | Secure chat with your other machines |
| `c` / `q` | Cycle theme / quit |

Before you've created a cluster, the TUI shows a welcome screen instead: **`[u]`** sets up
PlaceContext on this machine, **`[j]`** joins one you already run.

## If something doesn't come up

| What you see | What to do |
|---|---|
| Portal won't answer on :7700 | `pctl status` to see what's running, then `pctl logs -f` for errors |
| TUI says "cluster not reachable" | It's stopped — `pctl ensure` starts it back up |
| Broke after a Docker cleanup | `pctl ensure` repairs it; if it can't, it tells you plainly |
| Red banner about the database | `pctl deploy` (or the TUI's `[u]`) brings it up to date |
| Not sure anything is healthy | `pctl doctor --go-live` runs the full check |

## Where to go next

- **Projects** — the top-level unit everything hangs off.
- **Jobs and artifacts** — write code, get results you can chart.
- **Cluster and nodes** — grow from one laptop to a fleet of machines.
- **MCP and agents** — connect Claude and let an agent do the work.
