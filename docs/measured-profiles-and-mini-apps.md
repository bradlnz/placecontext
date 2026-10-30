# Measured profiles and mini apps

## Smart dashboards

The dashboard is now indicator-first. A profile is a bounded group of **real chart measures from the same recorded source**, not a collection of unrelated attractive charts. Each named measure produces an indicator containing its observed statistic (mean by default, or a target's configured statistic), displayed-value count, source reference, timestamp, freshness and data-confidence band. The overview shows those measured indicators before the underlying charts. Clicking one opens the source evidence.

Charts may represent a customer data source, but their category labels do **not** establish customer identity. A future per-customer profile must use a stable authorised customer key, documented joins and source completeness. Do not synthesise customer counts, churn probabilities, trends, revenue or units from chart titles. Jev's decision support remains separate from measurement/data confidence. Decisions must be invalidated when their chart evidence or target changes.

A possible customer profile could contain retention, contact volume, call resolution and commercial value indicators **only when each is backed by the correct source records and definitions**. Missing data is visible, not estimated as zero.

## Agent Store mini apps

The primary path is:

1. Choose a mini app for an outcome.
2. Select a discovered, authorised project source connector (if one exists). If no source is found, use **Connect project data**.
3. Click **Generate workflow** and enter a 3–100 character name.
4. Continue to Build with a **draft request** carrying only the mini app outcome, workflow name and source **reference**, not sampled project records.

Build checks project permissions, schema and freshness again and presents a draft for review. No workflow is activated, no external operation is executed, and no model request is made simply by browsing the Store. The existing advanced configuration remains available under a disclosure. The Store does not claim that an arbitrary telephony, CRM or commerce API is connected merely because its mini app exists.

This is a named draft hand-off, not background workflow generation. The Build user must explicitly send or approve their request to create a workflow.
