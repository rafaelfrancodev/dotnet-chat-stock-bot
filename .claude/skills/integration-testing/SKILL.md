---
name: integration-testing
description: Integration testing approach — WebApplicationFactory, in-memory/SQLite/Testcontainers database, SignalR client tests, RabbitMQ testing strategy. Use when testing end-to-end flows, persistence, authentication, hub behavior, or when the user mentions integration tests or E2E.
---

# Integration Testing

## Stack

- Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory<Program>), xUnit, FluentAssertions.
- Database: `Testcontainers.MsSql` — a throwaway SQL Server container, the same provider the app runs on. Docker is already a prerequisite (see `docker-compose.dev.yml`). Swap the connection via a test factory override; skip the tests with a clear message when Docker is unavailable.
- RabbitMQ: substitute IStockQuoteRequester/consumer with fakes in most tests; optionally one Testcontainers-based test for the real broker round-trip if time allows.

## What to cover (highest value for this challenge)

1. Auth flow: register -> login -> authenticated request succeeds; anonymous hub connection rejected.
2. Message persistence: post message via hub/endpoint -> appears in GetLatestMessages, correct order, capped at 50.
3. Stock command flow: posting /stock=aapl.us publishes a broker request AND does not create a DB row.
4. SignalR round-trip: connect two HubConnection test clients to the same room; client A sends, client B receives (mirrors the "2 browser windows" evaluation).
5. Bot pipeline (fake HttpMessageHandler for Stooq): request message in -> formatted quote message out to the room.

## Conventions

- Shared CustomWebApplicationFactory with: test auth helpers, DB reset per test class, fake HttpMessageHandler for Stooq.
- Deterministic: no Task.Delay-based waiting; use TaskCompletionSource with timeout when awaiting hub callbacks.
- Keep the suite fast enough to run on every task (dotnet test tests/Chat.IntegrationTests).
