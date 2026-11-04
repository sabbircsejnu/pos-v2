---
name: session-handoff
description: Write a short (≤300 words) session handoff for resuming IraniMart POS v2 work later, with no secrets.
disable-model-invocation: true
argument-hint: "[save]"
---

# Session handoff

Use the template `.claude/context/SESSION_HANDOFF.template.md`. Contents (≤300 words, no secret values):
- Objective and explicitly approved decisions
- Branch and working-tree summary (from actual `git status`)
- Changed files and verified commands/results
- Open risks/blockers/assumptions
- The immediate next command or check

Reply in chat by default. Write `.claude/context/SESSION_HANDOFF.md` only when the user passes `save` or asks for it; read any existing handoff before overwriting it.
