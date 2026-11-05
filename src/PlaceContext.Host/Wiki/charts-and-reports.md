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
as bar, line, or pie charts in Analytics.

Charts load their renderer on demand. Keep queries small and aggregate the data before charting it;
the page can display the chart as soon as its data and renderer are ready.

### TV mode

Choose **TV mode** in Analytics to present the current dashboard without navigation or
editing controls. Charts expand for a large display, and the existing live refresh keeps
running every 10 seconds. The header shows whether live refresh is active or paused.
Use **Exit TV mode** or **Escape** to return to the normal view.

The browser enters fullscreen when permitted. Otherwise the presentation fills the app
window. Dashboards with many charts remain scrollable; TV mode does not rotate charts
or change your live-refresh preference.
