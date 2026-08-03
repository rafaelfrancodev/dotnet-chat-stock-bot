---
name: clean-code
description: Clean Code conventions for all C# written in this repo — naming, method size, nullability, async, error handling, comments, commit hygiene. Apply to EVERY file created or edited, and during any code review or refactor request.
---

# Clean Code (C# / .NET)

## Naming & structure

- Intention-revealing names using the ubiquitous language (stockCode, not sc; GetLatestMessagesQuery, not MsgQ).
- Methods small and single-purpose (~20 lines guideline); one level of abstraction per method.
- Guard clauses over nested ifs. Early return.
- No magic numbers/strings: const int LatestMessagesCount = 50; queue names in MessagingConstants; config keys in options classes.

## Language usage

- <Nullable>enable</Nullable> everywhere; no ! suppressions without a justifying comment.
- async/await all the way; never .Result/.Wait(); suffix async methods with Async; accept CancellationToken.
- record for DTOs/value objects; sealed by default; file-scoped namespaces.
- Options pattern (IOptions<StooqOptions>) instead of raw IConfiguration access.

## Error handling

- Exceptions for exceptional situations only; expected failures via Result pattern.
- Never swallow exceptions. Catch narrowly, log with context (structured logging: logger.LogWarning("Stock quote failed for {StockCode}", code)).
- Bot must survive bad input: malformed CSV, unknown ticker (N/D from Stooq), broker hiccups -> log + friendly error message to the room, never crash.

## Comments & docs

- Code should explain itself; comments explain WHY, not what.
- XML doc comments on public Application/Domain contracts.

## Hygiene

- Remove dead code, unused usings, commented-out blocks before committing.
- .editorconfig enforced; run dotnet format before finishing a task.
- Small, focused commits with imperative messages ("Add stock command parser", not "changes"). Commit after each completed task — the challenge requires local Git history.
