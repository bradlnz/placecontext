# Agents, people, and decisions

PlaceContext makes a decision part of an inspectable process: gather evidence, record a response, and define how that response controls later work.

## Choose the right kind of step

| Step | Purpose | Result |
| --- | --- | --- |
| Agent task | Perform work or prepare a recommendation | Recorded output from an automatic or external agent |
| Agent in the loop | Add a separate review or assessment | Recorded review, or configured AI verification result |
| Human in the loop | Ask for judgement, approval, or missing information | Written answer or JSON boolean |
| Scoring gate | Assess evidence against weighted criteria | Score, pass result, blockers, and evidence metadata |
| Condition | Choose the next path from recorded values | True or false branch |

## Record the human response

Configure a Yes/No human step with a response field such as `approved`. The run pauses and shows a request for input. When an authorized person responds, the answer is persisted and the workflow can continue.

The notification bell highlights **Needs input**, and the run view shows the pending request. These are in-app notifications, not an email escalation or assignment system.

## Gate the action explicitly

Use `eq:approved:true` on an action that requires approval. A No response stores `false`; it is not an automatic cancellation. Conditional paths rejoin, so later approval-dependent actions also need the appropriate condition.

| Condition | Meaning |
| --- | --- |
| `eq:approved:true` | Continue only after a recorded Yes |
| `eq:approved:false` | Select the follow-up path after No |
| `gte:agent_response.noul:0.9` | Apply a configured AI probability threshold |
| `eq:release_review.passed:true` | Continue after the scoring gate passes |

## Separate recommendation from approval

An AI recommendation supplies evidence for the process. A model verification result is an assessment. Neither is a person's authorization. Configure the human step and downstream conditions when an action requires human judgement.

AI verification and scoring require their configured service. A verification error fails the step rather than approving it.

## Inspect the whole path

Run history shows recorded output, completed and skipped steps, and errors. Use **Pre-run checks** to inspect readiness before execution. These checks inspect definitions; they do not execute jobs or prove that provider credentials work.

![Workflow with human input and explicit conditional paths](/assets/workflow-decision-flow.png)

See the [platform reference](https://github.com/bradlnz/placecontext/blob/main/docs/platform-reference.md#decisions-and-human-approval) for scoring, repeat-flow limits, and detailed condition semantics.
