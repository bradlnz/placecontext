# Pausing a chain for input

A normal chain stage checks its jobs' declared required parameters before
starting any job in that stage. Missing values pause the run as `AwaitingInput`;
later stages stay pending. Zero and false are values, while null and blank text
are missing. Parameters may use dotted paths, such as
`funding_inputs.client_cash`, to address nested envelope fields.

The current envelope, stage index, parameter overrides and input request are
stored in the database. The input request is encrypted with the chain-run
purpose. There is no scheduled wake-up until the requested values are submitted.
The funding job must declare its fields; the runner does not infer required
inputs from arbitrary job output or copy its saved example payload.

`get_job_chain_submission` includes `inputRequest` while the run is paused.
Submit that request through the run page or the `resume_job_chain` MCP tool:

```json
{
  "chainRunId": "<existing run UUID>",
  "inputRequestId": "<inputRequest.id>",
  "valuesJson": "{\"12\":{\"funding_inputs.client_cash\":800000}}"
}
```

The outer key in `valuesJson` is the requested step index. The inner keys are
declared parameter names, including their dotted paths. Existing fields are
preserved; unknown steps/fields and incomplete required inputs are rejected.
A database row lock serializes submissions. Repeating the same request ID and
values returns the current status; conflicting values are rejected. Submission
requires `jobs.run` permission in the run's tenant.

A successful submission schedules the existing durable continuation worker.
It resumes the same run at the paused stage, without rerunning earlier stages.
The funding calculator still validates ranges and financial constraints.

Deploy the `AddChainInputRequest` migration before starting the updated host.
To run the database regression against an isolated PostgreSQL database:

```sh
PLACECONTEXT_TEST_POSTGRES='Host=localhost;Port=5433;Database=placecontext_test;Username=postgres;Password=postgres' \
  dotnet test tests/PlaceContext.Infrastructure.Tests --filter FullyQualifiedName~ChainInputResumeTests
```
