---
description: Implement a task from docs/PLAN.md (code + unit tests + build/test/format green + commit)
argument-hint: <task number or short description>
---

Use the implementer agent.

Task to implement: $ARGUMENTS

Steps:
1. Read docs/PLAN.md and locate the task; read CLAUDE.md and all relevant skills (clean-architecture, ddd-patterns, cqrs-command-handlers, clean-code, security, performance, unit-testing).
2. Implement the task respecting layer boundaries and challenge constraints (stock commands never persisted; last 50 messages by timestamp; bot decoupled via RabbitMQ; hubs authorized).
3. Write unit tests for the new behavior (happy path + at least one edge case).
4. Run: dotnet build; dotnet test; dotnet format. Fix all issues.
5. Mark the task done in docs/PLAN.md, commit with an imperative message.
6. If setup/commands/structure changed, run the docs-maintainer agent afterwards (or tell me to run /update-readme and /update-claude-md).
