Author it the way the Claude Code CLI expects a skill:
- Write it to `.claude/skills/{{skillName}}/SKILL.md`.
- Begin with YAML frontmatter: `name` (kebab-case) and a precise `description` that states *when*
  to use the skill (the CLI uses this to decide when to trigger it).
- Then concrete, CLI-runnable steps (the exact commands, files, and checks), not generic advice.