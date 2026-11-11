# Build your first workflow

Start with a small review process: read a few records, gather a recommendation, ask a person, and prepare the appropriate next step. The screenshots below come from the real PlaceContext application using sample customer data.

## Prepare a project table

Create a table or import a CSV under **Data → Tables**. For an onboarding example, include a customer name, account status, and plan. Verify the imported records before adding automation.

## Add a query

Create a workflow and add **Query data**. Browse the project's tables and columns, select the fields, and preview the SQL. Use an output field such as `accounts`; later steps can read `accounts.rows`.

![Query data in the actual workflow editor](/assets/workflow-query-data.png)

The query is read-only. Narrow or aggregate large datasets: workflow execution rejects truncated results, duplicate column names, and results larger than 1 MiB.

## Ask for a recommendation

Add an **Agent task** and describe the task and expected result. Choose **Automatic** with a configured model, or **External agent** to wait for a supplied response. Save its output to a field such as `recommendation`.

The captured example used an external-agent response. The screenshot demonstrates the workflow and handoff; it does not demonstrate an automatic provider run.

## Add a human decision

Add **Human in the loop**, choose Yes/No, and name the response field `approved`. Ask the reviewer to consider the query results and recommendation before answering.

![A workflow paused for human approval](/assets/workflow-human-approval.png)

## Define both outcomes

Add a follow-up query or job with condition `eq:approved:true` for the approved outcome. Use `eq:approved:false` for a separate follow-up path, or configure a false branch.

A No answer completes the human step with `false`. It does not cancel the whole workflow. Both paths rejoin, so every later action that requires approval must depend on the recorded answer.

![The complete workflow with explicit decision paths](/assets/workflow-decision-flow.png)

## Run and inspect

Use **Pre-run checks** to inspect the definition, then run it. In **Runs**, open the active run, inspect each step, and answer the human checkpoint. Check that the chosen path succeeded and the unselected path was skipped.

![Completed run with the unselected query skipped](/assets/workflow-run-completed.png)

You can insert further queries wherever the process needs current evidence. Once both outcomes work with sample data, add jobs, schedules, or queue delivery as needed.

[Screenshot provenance](https://github.com/bradlnz/placecontext/blob/main/docs/images/workflow-screenshots.md) · [Decisions and approvals](/docs/decisions/)
