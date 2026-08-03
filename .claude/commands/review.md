---
description: Run a full standards review (architecture, DDD, clean code, security, performance, tests, challenge compliance) on recent changes
argument-hint: [optional scope, e.g. "src/Chat.Bot" or "last commit"]
---

Use the code-reviewer agent (read-only).

Scope: $ARGUMENTS (if empty: uncommitted changes plus the last commit).

Produce the severity-grouped report (Blocker / Should fix / Nit) with file:line references and a final APPROVE / REQUEST CHANGES verdict, including the challenge compliance spot-check. If there are Blockers, list them as concrete follow-up tasks I can feed to /implement.
