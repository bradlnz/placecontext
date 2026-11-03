Author it the way the OpenAI Codex CLI expects:
- Write a reusable prompt to `.codex/prompts/{{skillName}}.md` (Codex loads project prompts from there).
- Start with a one-line summary of when to use it, then concrete, CLI-runnable steps — the exact
  commands, files, and checks — matching how this project already does things, not generic advice.
- If the guidance should always apply (not just on demand), add it to the project's `AGENTS.md` instead.