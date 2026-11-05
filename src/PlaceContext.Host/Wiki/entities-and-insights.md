# Entities and insights

*Give project data business names and relationships.*

## Define an entity

Open **Data → Entities** and create an entity from a project table. Set:

- a display name;
- the backing table;
- a label or key column;
- optional relationships to other entities.

The entity then appears under the Data menu and can be opened as its own business view.

## Entity views

An entity provides:

- **Records** for browsing table rows and opening record details;
- **Graph** for exploring linked records, runs, and artifacts;
- **Analytics** for charts over the entity's data.

Entity keys are indexed for workspace search. Runs and artifacts that reference known keys can
be linked into the graph, making related work easier to find.

The entity catalogue stays focused on entity definitions, tags, and declared relationships. Raw
linked-value groups are used to build graph connections but are not listed on the Entities page.

## Data graph

Open **Data → Graph** to explore project tables and their data mappings.
Stored files appear in a separate paginated artifact table, with links to preview a file or map
its source's primary JSON output. Jobs, workflows, their outputs, runs, and activity logs are not graph nodes.

Pin important entities to the Dashboard for quick access.

Dashboard entity cards show links and estimated row counts immediately. Select **Load summaries**
to calculate the category breakdowns when you need them; large tables can take longer to summarize.
