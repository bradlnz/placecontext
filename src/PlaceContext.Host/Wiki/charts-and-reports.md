# Charts and analytics

*Turn job results and project data into readable views.*

## Job output

Choose the job's return type to control its primary artifact:

| Return type | Result |
|---|---|
| **JSON** | Stored as JSON and displayed in a readable form |
| **Table** | Rendered as an HTML table report |
| **Chart** | Rendered as a chart artifact |
| **HTML** | Stored as an openable HTML document |
| **CSV** | Stored as a downloadable CSV |
| **Text** | Stored as plain text |
| **PDF / Image / Video** | Stores the matching file written by the job |

JSON results containing a small numeric series can also be charted directly in the run detail.
Keep standard output clean and send diagnostic messages to standard error.

## SQL analytics

Open **Data → Analytics** to create charts from project tables. A chart uses a read-only `SELECT`:

```sql
SELECT status, count(*)
FROM jobs
GROUP BY status
ORDER BY status;
```

The first column supplies labels and numeric columns supply the series. Charts can be displayed
as bar, line, or pie charts and also appear on the Dashboard.

Charts load their renderer on demand. Keep queries small and aggregate the data before charting it;
the page can display the chart as soon as its data and renderer are ready.

## Data profiles

Open **Data profiles** for an immediate view of the project's saved chart snapshots. The
unsaved project overview selects up to 12 charts; **Choose data** lets you change the selection.
You can inspect sources before creating a saved dashboard. **Save this view** keeps your layout
and question private to your account in this project.

- **Overview** puts profiles that need attention first, then older snapshots.
- **Needs attention** focuses on unusable, stale or structurally ambiguous evidence.
- **Explore values** follows your saved chart order and gives the chart more prominence.

Select a profile to see its source reference, generation time, displayed minimum and maximum,
and evidence checks. Expand a check for the reason and next investigation. Use the data table
to inspect the exact displayed values; chart categories are not source row counts.

### Confidence and freshness

| Band | Meaning in the current implementation |
|---|---|
| **Unknown** | No usable displayed values, or a selected source chart is missing |
| **Low** | Usable values exist, but the snapshot is stale, has an unknown or invalid timestamp, or has ambiguous labels or invalid structure |
| **Medium** | Displayed values are usable and no attention check failed; source coverage remains unverified |
| **High** | Reserved for verified evidence; the current snapshot contract cannot earn this band |

A snapshot is **Current** for 24 hours after generation, **Aging** for up to 7 days, then
**Stale**. These are default review windows, not a promise about the underlying records or a
business SLA. The assessment time is shown in the profile. Reload snapshots to reassess.

Saved charts do not retain the full source dataset, query provenance, source null rates or
verified sampling coverage. Chart generation can also omit categories or normalize values.
The checks describe the stored snapshot and cannot certify that source data is complete or
correct. Open Analytics to inspect the source and regenerate it when needed. Reloading a
dashboard does not rerun the source query.

### Investigate with Jev

Suggested questions are grounded in the selected profile's displayed categories and measures.
Choosing a suggestion fills the question box; it does not make a model request. Edit the
yes/no question and send it when ready. Jev receives the chart snapshots selected for the view,
not unseen source rows. Its probability estimates the answer to your question and remains
separate from the evidence confidence band.

Changing the question, profile focus, view or chart data clears the prior displayed assessment.
Missing source charts block analysis until the view is repaired. A changed snapshot fingerprint
requires reloading before another analysis. Model analysis requires the project's configured
Jev service and may incur provider usage; exploring profiles does not.
