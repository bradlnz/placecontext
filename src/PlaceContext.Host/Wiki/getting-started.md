# Getting started

*Install PlaceContext on your own machine, sign in to the portal, and learn your way around.*

## What you get

PlaceContext runs on your own hardware and keeps everything there. It gives every project
a place to run your code as jobs, keep its own database, tag and organise its data into
business views, and be worked on by an AI agent. Every job run produces an openable
**artifact**, and the portal draws charts straight from those results — so
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

The left-hand nav has two parts. The top group is always there:

| Page | What you do there |
|---|---|
| **Dashboard** | Live stats, recent runs, and pinned entity views for the current project |
| **Artifacts** | A file viewer over every result any job has produced — JSON, tables, charts, PDFs — with version history |
| **Observability** | Live job-run history and run detail, refreshed as runs progress |
| **MCP Server** | Watch, live, every action an AI agent takes against your projects |

Below that, a **Workspace** group: **Projects overview** (all projects at a glance),
**Onboarding**, **Wiki** (this documentation), **Settings** (white-labelling and locality),
and **About**.

When a project is selected, its own pages appear at the top of the nav:

| Project page | What you do there |
|---|---|
| **Jobs** | Author, edit, and run the project's jobs |
| **Chains** | Wire jobs into multi-step pipelines that run in sequence |
| **Schedules** | Cron and event triggers that fire jobs automatically |
| **Data** | The project's own database — with **Tables**, **Analytics**, **Data map**, and **Entities** sub-tabs |
| **Vault** | Encrypted secrets the jobs use at run time |

Once you've tagged data into entities, each **business view** (e.g. *Sites*) also shows up
as its own menu item under a **Business** heading — see *Entities and insights*.

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
- **Entities and insights** — tag your data so it organises itself into business views.
- **Cluster and nodes** — grow from one laptop to a fleet of machines.
- **MCP and agents** — connect Claude and let an agent do the work.
