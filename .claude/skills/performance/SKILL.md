---
name: performance
description: Performance and resource-consumption practices for the chat app, SignalR, EF Core, RabbitMQ and the bot. Apply when writing queries, hub code, broker consumers, HTTP clients, or when the user mentions performance, resources, memory, or scalability. The challenge explicitly says "Pay attention if your chat is consuming too many resources."
---

# Performance & Resource Usage

## Database / EF Core

- "Last 50 messages": single indexed query — Where(roomId).OrderByDescending(Timestamp).Take(50), then reverse in memory for display order. Index on (ChatRoomId, Timestamp).
- AsNoTracking() for all read queries.
- Project to DTOs inside the query (Select(new MessageDto...)) — don't load entities to map later.
- No N+1: watch navigation property access in loops.

## SignalR

- Broadcast to Groups(roomId), not Clients.All — essential for the multi-room bonus.
- Keep payloads small (DTOs with only what the UI renders).
- No per-connection state beyond room membership; clean up in OnDisconnectedAsync.

## HTTP (Stooq client)

- IHttpClientFactory typed client — never new HttpClient() per request (socket exhaustion).
- Timeout (e.g., 10s) + retry with backoff (Polly) for transient failures.
- The CSV is tiny, but keep habits sane: no unbounded buffering elsewhere.

## RabbitMQ / bot

- One long-lived connection per process; channels per consumer. Never connect per message.
- Consumer with sensible prefetch (1-10); ack after successful processing; dead-letter or log poison messages instead of infinite requeue.
- Bot is a BackgroundService — fully async, no blocking calls, honors stoppingToken.

## General

- Measure before micro-optimizing; correctness and clarity first.
- Log at appropriate levels; no per-message Information logging that floods output.
