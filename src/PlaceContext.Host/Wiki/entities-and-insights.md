# Entities and insights

*Tag your data into entities and PlaceContext organises it for you — each becomes a business view with records, a relationship graph, and analytics, and every job run that mentions it links itself in automatically.*

## The idea

Jobs load rows into your project's tables and produce artifacts. On its own that's just data.
An **entity** turns a table into something the business reads: name it *Sites*, point it at the
`sites` table, and PlaceContext gives it a dedicated menu item with records, insights, a graph,
and charts. The promise is simple — **tag data and it organises itself into business insights.**

## Define an entity

Open **Data → Entities** and add one:

1. **Name** — what the business calls it (*Sites*, *Feasibility*, *Customers*).
2. **Table** — the project table it's backed by.
3. **Label column** — the human-readable key for each record (an address, a name). If you leave
   it blank, PlaceContext picks the first sensible text column.
4. **Relations** *(optional)* — link a column to another entity's key, e.g. *Site Scores.address*
   → *Sites.address*. Relations are what weave separate entities into one graph.

Once saved, the entity appears in the nav under a **Business** heading, alongside the project's
own pages.

## What a business view gives you

Open an entity and you get three tabs:

- **Records** — the rows, with auto-generated insights across the top (counts, distributions,
  averages) and each record openable in a side panel that links out to its related entities.
- **Graph** — a force-directed "brain" of everything connected to this entity: its records, the
  entities they relate to, and the job runs and artifacts that reference them. Click any node to
  centre it and highlight the artifacts reachable from it. Full-screen and node search are built
  in.
- **Analytics** — SQL-backed charts over the entity's table (see *Charts and analytics*), seeded
  with a few sensible defaults the first time you open the tab.

## Tagging happens automatically

You don't tag records by hand. When a job run completes, PlaceContext scans its output against
every entity's keys and links the matches — so a run whose JSON result contains a known site
address, or a **PDF or HTML** document that mentions one, is tied to that site, that job, and
that artifact. That link tree is what the graph draws.

This means the intelligence compounds on its own: schedule a job that refreshes `sites` nightly,
and its runs, artifacts, and the affected records all keep linking themselves together with no
extra work.

## Indexed for search

Every tagged key is indexed. Open the workspace search (**⌘K** / the search bar) and typing a
record's key returns it as a node — selecting it drops you straight onto that entity's graph,
scoped to just that record's neighbourhood. Artifacts are searchable there too, opening in the
Artifacts viewer.

## Pin the ones that matter

An entity you care about can be **pinned to the Dashboard** for one-click access, where it shows
a mini distribution bar so you can read the shape of the data without opening the full view.

## A typical flow

1. A job loads or refreshes rows into a project table.
2. You define an entity over that table (once), with relations to any it connects to.
3. Future runs tag themselves against it automatically — including PDF/HTML documents.
4. The business opens the entity's view: records, graph, and analytics, all kept current by the
   jobs behind them.
