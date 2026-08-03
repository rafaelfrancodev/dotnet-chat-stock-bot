---
name: cqrs-command-handlers
description: Implement application use cases as Commands/Queries with dedicated handlers (CQRS-lite, MediatR or hand-rolled mediator). Use whenever adding a new feature, endpoint, hub method, or use case; whenever the user says "command handler", "use case", "MediatR", "pipeline", or "validation".
---

# CQRS & Command Handlers

## Shape of a use case

Every use case = Request + Handler + (optional) Validator, in Chat.Application/Features/<Feature>/:

```
Features/
  Messages/
    PostMessage/        PostMessageCommand.cs, PostMessageHandler.cs, PostMessageValidator.cs
    GetLatestMessages/  GetLatestMessagesQuery.cs, GetLatestMessagesHandler.cs
  StockCommands/
    RequestStockQuote/  RequestStockQuoteCommand.cs, RequestStockQuoteHandler.cs
  Rooms/
    CreateRoom/, JoinRoom/, ListRooms/
```

## Rules

1. Commands mutate and return minimal data (id/Result). Queries read, never mutate, and may use optimized read models.
2. One handler per request. Handlers orchestrate: load aggregate -> invoke domain behavior -> persist -> publish events/notifications. No business rules inside the handler itself.
3. Validation with FluentValidation running in a pipeline behavior BEFORE the handler. Expected failures return a Result — do not throw for validation.
4. SignalR hubs and controllers are thin: parse input -> send request via mediator -> map result. Hub methods should be ~5 lines.
5. The /stock= flow: PostMessageHandler (or a message parser) detects the command, short-circuits persistence, and dispatches RequestStockQuoteCommand, which publishes to RabbitMQ through IStockQuoteRequester.
6. CancellationToken flows through every handler and I/O call.

## Result pattern

Use a small Result/Result<T> (Success/Failure + Error with code and message). Map to ProblemDetails or hub error callbacks at the edge.

## Testing hook

Handlers depend only on interfaces -> trivially unit-testable with fakes/mocks. Every new handler ships with at least one happy-path and one failure-path unit test (see unit-testing skill).
