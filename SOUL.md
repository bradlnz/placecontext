# SOUL

## Scope
This repository supports an agent-first operating model in the **Agents** workspace. The
implementation focus is: keep agent management predictable, visually explicit, and easy to
navigate.

## Agent roles

- **Command Agent**: orchestrates job assignment and high-level work flow decisions.
- **Worker Agent**: executes delegated actions against jobs and job chains.
- **Work Agent (UI view)**: visual card representing run history and outcomes in the Work tab.

## Non-goals

- Avoid changing orchestration behavior without first ensuring backward compatibility.
- Avoid UI changes that make the canvas or template area non-scannable at typical viewport widths.

## Operating expectations

- Keep the canvas interaction smooth (draggable nodes, visible connection lines).
- Keep side panels fully opaque and readable.
- Keep agent card sizing stable so the layout does not jitter as data changes.
