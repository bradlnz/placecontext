# Connect, query, and use your data

Data is the foundation of a PlaceContext project. Connect sources, prepare records for processing, keep accepted outputs in project tables, and use queries to drive the next action.

## Connect a source

Open **Data → Sources**. Source setup follows **Connection → Fields → Prepare & queue**. Discover accessible datasets, choose a stable key, approve fields, and select a destination queue with a processing workflow.

Connectors cover databases, object storage, lakes, warehouses, and API/event feeds. The platform reference lists PostgreSQL, MySQL, SQL Server, S3/MinIO, Azure Data Lake, Fabric/OneLake, Snowflake, and Databricks. Setup and usable datasets depend on the connector and upstream permissions.

Preparation retains approved fields, supports explicit field redaction, and deduplicates records. Missing required values and conflicting records are held for correction. The linked workflow performs subsequent processing; creating the source does not create that workflow automatically.

## Work in project SQL tables

Under **Data → Tables**, create tables, import CSV, browse records, and work with SQL. Project schemas and database roles separate these tables from other projects and platform tables.

Tables are the source of truth for processed data. Search and vector indexes are derived representations, with embeddings requiring a configured provider.

## Query at any workflow stage

Add **Query data** wherever the workflow needs evidence. Browse columns, inspect sample values, apply filters, and preview SQL before saving. The query runs in a read-only project transaction and requires `data.read`.

Choose an output field such as `query_data`. Results include `columns`, named-object `rows`, `row_count`, and `truncated: false`. Workflow execution rejects incomplete or oversized results rather than passing partial evidence to a decision.

![Project data query and preview](/assets/workflow-query-data.png)

## Map output into tables

Use **Data map** to connect a job or workflow's JSON output to typed columns. Choose the row path, map fields, and enable the mapping. Completed runs append matching rows with a run ID and ingestion timestamp for traceability.

Artifact files stay in artifact storage. Mapping primary JSON output does not import arbitrary file contents.

## Explore and report

Entity views bring related records together. Saved SQL queries and charts help you inspect project data. Live charts poll their read-only queries while open; use aggregates for totals instead of treating sampled previews as complete datasets.

## Validate before acting

The **Validate data contract** job can check required fields, types, row counts, null rates, and unique keys. Put it before downstream writes or delivery. Failed validation stops the workflow and returns structured evidence.

Read the [project data reference](https://github.com/bradlnz/placecontext/blob/main/src/PlaceContext.Host/Wiki/project-data.md) for connector limits, queue semantics, query limits, and worker data access.
