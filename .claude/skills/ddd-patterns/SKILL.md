---
name: ddd-patterns
description: Apply DDD tactical patterns (entities, value objects, aggregates, domain events, repositories, ubiquitous language) when modeling or changing domain code. Use whenever creating or editing anything in the Domain project, designing new business concepts (ChatRoom, Message, StockCommand), or when the user mentions DDD, domain model, entity, aggregate, or invariants.
---

# DDD Tactical Patterns

## Ubiquitous language for this project

ChatRoom, Message, StockCommand, StockQuote, Participant (registered user). Use these names consistently in code, tests, commits, and docs.

## Aggregates

- ChatRoom is an aggregate root. Message is an aggregate referencing ChatRoomId by ID (preferred here, since the only query is "last 50 by room").
- Enforce invariants inside aggregates (message must have content, author, timestamp).
- Keep aggregates small; reference other aggregates by ID, never by object reference.

## Value Objects

- MessageContent (non-empty, max length, trimmed), StockCode (parsed from /stock=code, normalized to lower-case).
- Immutable, equality by value, validated at creation — invalid state must be unrepresentable.
- Use record types or sealed classes with private constructors + static Create() returning a Result.

## Domain rules specific to the challenge

- /stock=stock_code is a COMMAND, not a Message. Detect it before persistence; it must NOT be saved as a post (explicit challenge requirement).
- Bot-authored quote messages are broadcast to the room with "Bot" as the post owner; default decision: broadcast-only, not persisted (document this in README design decisions).
- "Last 50 messages ordered by timestamp" is a domain query concept — encapsulate it (GetLatestMessages(roomId, take: 50)), don't leak OrderByDescending().Take(50) everywhere.

## Domain Events

- Raise StockCommandReceived(roomId, stockCode, requestedBy) instead of calling RabbitMQ from the domain. An application handler dispatches it to the broker via an interface.

## Anti-patterns to reject

- Anemic entities with public setters everywhere.
- Domain classes referencing EF Core, DataAnnotations for business rules, or SignalR.
- Business logic inside hubs/controllers.
