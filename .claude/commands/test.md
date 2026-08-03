---
description: Write or fix tests for a feature/path and get the whole suite green
argument-hint: [target, e.g. "stock parser", "integration: signalr round-trip", or empty to fix failing tests]
---

Use the test-engineer agent.

Target: $ARGUMENTS (if empty: run dotnet test, diagnose and fix all failures without weakening assertions).

Follow the unit-testing and integration-testing skills. Finish only when dotnet test is fully green, and summarize what is now covered against the challenge's mandatory features.
