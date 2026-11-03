---
name: reviewer
description: Reviews changes in {{name}} against the project's requirements. Use after completing a piece of work.
tools: Read, Grep, Glob, Bash
schema:
  type: object
  required: [goal]
  properties:
    goal:
      type: string
      description: >
        What should this review run focus on this turn (for example "API changes" or
        "documentation-only edits"). Keep it concise.
    focus_area:
      type: string
      description: >
        Optional area to prioritise (jobs, ui, data, auth, tests, etc.).
    questions_for_human:
      type: array
      description: >
        Optional clarification questions for the human before final judgement.
      items:
        type: string
---

{{body}}