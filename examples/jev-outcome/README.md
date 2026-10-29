# Jev outcome demo

This example reviews a synthetic invoice and branches on Jev's confidence score.
It does not send invoices, make payments, or contact customers.

The saved workflow is **Jev outcome demo** in **Default project**. Run it with
`{"case":"correct"}` or `{"case":"incorrect"}`. With no input it uses the correct
invoice. The correct total is AUD 60; the incorrect example claims AUD 600.

| Stage | `DEMO_STAGE` | Gate |
| --- | --- | --- |
| Prepare invoice | `prepare` | Always |
| Jev review | `review` | Always |
| Approved (true path) | `approved` | `gte:review.noul:0.8` |
| Needs review (false path) | `needs_review` | The condition above is false |
| Summary (rejoin) | `summary` | After either path completes |

Approved and Needs review are the two paths of one condition. The editor displays
them separately and rejoins them before Summary. **Add parallel job** runs jobs
together within one path; **Edit false path** changes the alternative route.

All five Python jobs use `pipeline.py`. Only the review job enables network
egress. Add `TYPESAFE_API_KEY` to the project's vault; the worker receives it at
runtime. The review sends only the synthetic invoice to TypeSafe's Jev endpoint.
Missing credentials, provider errors, and invalid scores fail the workflow.
The selected branch and score appear in the workflow's final JSON output and run
history. Downloadable artifacts require a configured object store; this local
instance currently has object storage disabled.

This uses a Python review job because the built-in Jev agent step currently
reads host configuration rather than project vault secrets. The example's 0.8
threshold is illustrative; a model score is not a guarantee of correctness.

Run the local checks without calling Jev:

```bash
python -m unittest discover -s examples/jev-outcome -v
```
