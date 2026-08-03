---
name: unit-testing
description: How to write unit tests in this repo (xUnit, naming, AAA, mocking, what to cover). Use whenever implementing any handler, domain type, parser, or service; whenever the user asks for tests, coverage, or TDD. Every feature task is only DONE when its unit tests exist and pass.
---

# Unit Testing

## Stack & layout

- xUnit + FluentAssertions + NSubstitute (pick one mocking lib, stay consistent).
- tests/Chat.UnitTests/ mirrors source structure: Domain/, Application/Features/...

## Conventions

- Naming: MethodOrScenario_Condition_ExpectedOutcome (e.g., Create_EmptyContent_ReturnsFailure, Handle_StockCommand_DoesNotPersistMessage).
- AAA (Arrange/Act/Assert) with blank-line separation; one logical assertion focus per test.
- No I/O, no real DB, no real broker, no sleeps. Pure and fast (<100ms each).
- Use builders/object mothers for aggregates to keep arrange sections short.

## Priority targets for THIS challenge (what reviewers will look at)

1. Stock command parser: /stock=aapl.us recognized; empty /stock=, /STOCK=X casing, garbage input, plain messages -> correct classification.
2. CSV parsing in the bot: valid Stooq line -> "AAPL.US quote is $93.42 per share"; N/D values -> graceful "not found" message; malformed CSV -> handled error.
3. Domain invariants: MessageContent validation, StockCode value object.
4. Handlers: PostMessageHandler persists normal messages, short-circuits stock commands (verify repository NOT called, broker publisher called).
5. Last-50 ordering logic wherever in-memory shaping exists.

## Definition of done

- New/changed behavior covered by at least happy path + one edge case.
- dotnet test green locally before task completion; test command documented in README.
