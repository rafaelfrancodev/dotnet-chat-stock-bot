---
name: docs-maintenance
description: How to write and keep README.md and CLAUDE.md up to date. Use whenever a feature is completed, architecture changes, setup steps change, or the user asks to update docs, readme, or claude.md. README is a DELIVERABLE requirement of this challenge.
---

# Documentation Maintenance

## README.md (deliverable — reviewers will follow it literally)

Required sections, in order:
1. Project overview — one paragraph + feature list mapped to the mandatory features.
2. Architecture — solution tree, why Clean Architecture/DDD/CQRS, how the bot is decoupled (RabbitMQ), simple flow diagram of the /stock command.
3. Prerequisites — .NET SDK version, Docker (RabbitMQ/DB), exact versions.
4. Setup & run — copy-pasteable commands: docker compose up -d, migrations, dotnet run for Web AND Bot (two processes), URLs, seeded test users if any.
5. How to test the chat — the 2-browsers/2-users script reviewers will perform, including a /stock=aapl.us example.
6. Running tests — dotnet test commands for unit and integration suites.
7. Bonus features completed — explicit checklist (multiple rooms, .NET Identity, bot error handling, installer) with done/not done. The challenge asks for this explicitly.
8. Design decisions & trade-offs — short; include the "stock command is not persisted" note.

Rule: every command must be verified by actually running it before writing it down. No stale ports, no missing env vars.

## CLAUDE.md (project memory for Claude Code)

Keep it current after every architectural or convention change:
- Solution structure and layer responsibilities
- Conventions (naming, Result pattern, test naming)
- Build/test/run commands
- Current status: which plan tasks are done / in progress / pending
- Gotchas discovered during development (Stooq quirks, RabbitMQ startup ordering, etc.)

Update triggers: new project added, new convention adopted, command changed, task completed. Keep it under ~150 lines — link to docs/ for details rather than bloating it.
