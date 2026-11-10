# Customer onboarding: query to decision

The working example shown in the main README. It uses sample project data, an external-agent
handoff, a human Yes/No response, and conditional queries. It prepares a result for the next action;
it does not contact customers or write to an external CRM.

**Query data** can be used at any workflow stage. This example uses it before agent review to
gather evidence and after human approval to prepare the selected outcome.

## Try it

1. Import [workflow.json](workflow.json) through **Settings → Backup**. This creates the
   **Customer operations** project and the **Customer onboarding · query to decision** workflow.
   Importing does not run it.
2. In that project's Data area, create a table named `customer_accounts`. Import
   [customer_accounts.csv](customer_accounts.csv), using text columns for `customer_name`, `plan`,
   and `status`, and a numeric column for `monthly_value`.
3. Open the workflow and select its first **Query data** step. Preview the query: it should return
   Summit Retail Group, Northwind Labs, and Bluebird Studio. Save the step if you change the query.
4. Run the workflow. The **Agent task** uses **External agent** mode and waits for a written review
   of `accounts.rows`. Read the accumulated context, then submit a recommendation through the run
   panel or a connected agent. Alternatively, configure Automatic with an enabled model connection.
5. Answer the human checkpoint and confirm your response. Try a second run with the other answer
   to compare the paths.

## What each stage does

| Stage | Input or condition | Result |
| --- | --- | --- |
| Query accounts | Monthly value at least 10,000, or status `Needs review` | Three rows in `accounts.rows`. |
| Agent review | The selected account rows | Written `recommendation` for the human reviewer. |
| Human decision | Account evidence and recommendation | Boolean `approved`. |
| Prepare handoff | `eq:approved:true` | Two active enterprise accounts in `handoff.rows`. |
| Hold for follow-up | `eq:approved:false` | All three selected accounts in `held_accounts.rows`. |

Both conditions rejoin the workflow. A No response completes the human step and selects the hold
query; it is not cancellation. The unselected query is recorded as Skipped. Both paths should finish
with Succeeded status, with four completed stages and one skipped stage.

Queries are read-only. The input query returns at most 20 rows, and each result keeps its row count
and column names. This example needs a running PlaceContext database and `data.read` access; it has
no code-job or QEMU steps. Human instructions work without a question-writing model. External-agent
mode requires a fresh response and does not call a model automatically.

The five companies and amounts are demonstration data. The thresholds illustrate branching;
choose rules suitable for your own process.
