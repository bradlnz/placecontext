# PURPOSE

## Why this workspace exists

The **Agents** workspace exists to keep execution work visible, controlled, and explainable:
commands and schedules should produce concrete job run artifacts, and those runs should be easy to monitor from a single board.

## Command Agent

- Owns orchestration decisions for a project.
- Maintains command-level context and routing between work items and available workers.
- Uses schedule/trigger inputs and periodic observations to prioritize and dispatch work.
- Does not execute job runs directly unless explicitly designed for direct command action.

## Worker Agents

- Specialize in a specific domain (MCP, jobs, schedules, analysis, chain operations, etc.).
- Execute delegated tasks through constrained capabilities and job allowlists.
- Return structured, auditable updates that can be represented as work items (status, attempts, shard outcomes).

## Work Board

- Tracks recent job run reports in bucketed state columns.
- Must reflect the latest run status for scheduled and manual jobs.
- Is treated as the operational mirror of execution state and must be updated whenever run status changes.
