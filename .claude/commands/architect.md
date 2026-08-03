---
description: Design or evolve the solution architecture and produce/refresh the implementation plan (docs/PLAN.md)
argument-hint: [optional focus, e.g. "messaging topology" or "initial scaffold"]
---

Use the architect agent for this.

Focus: $ARGUMENTS (if empty: full initial architecture for the challenge).

Steps:
1. Read CLAUDE.md, the challenge requirements section in CLAUDE.md, and existing code (if any).
2. Apply the clean-architecture, ddd-patterns and cqrs-command-handlers skills.
3. Produce or update:
   - Solution/project scaffold decisions (create projects with dotnet CLI if scaffolding).
   - Domain model outline (aggregates, value objects, events).
   - Messaging topology (queues, exchanges, message contracts between Web and Bot).
   - docs/PLAN.md: ordered task list with acceptance criteria; mandatory features first, bonuses after; each task small enough for one commit.
4. Summarize decisions and trade-offs, and list which CLAUDE.md sections must be updated (then update them).
