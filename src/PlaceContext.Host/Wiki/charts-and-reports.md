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
