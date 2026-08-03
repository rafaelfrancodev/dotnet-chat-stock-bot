# Architecture — Financial Chat (.NET challenge)

Two runnable processes, one solution, Clean Architecture + DDD tactical patterns + CQRS-lite.

| Process | Project | Responsibility |
| --- | --- | --- |
| Web host | `src/Chat.Web` | ASP.NET Core, Identity UI, SignalR hub, minimal Razor page, composition root |
| Bot worker | `src/Chat.Bot` | `BackgroundService` that consumes stock-quote requests, calls Stooq, publishes answers |

They share nothing at runtime except **RabbitMQ**. The bot has no reference to `Chat.Web`, no database
connection and no SignalR client — that is what "decoupled" means here.

---

## 1. Layers and the dependency rule

```
Chat.Web ─┐
          ├─> Chat.Infrastructure ─> Chat.Application ─> Chat.Domain
Chat.Bot ─┘
```

| Project | Contains | Must never contain |
| --- | --- | --- |
| `Chat.Domain` | Entities, aggregates, value objects, domain events, `Result`/`Error`, domain constants | Any package reference at all. The csproj is deliberately empty |
| `Chat.Application` | Commands/Queries + handlers + validators, wire contracts, abstractions (`IMessageRepository`, `IStockQuoteRequester`, `IChatNotifier`, ...), pipeline behaviors | EF Core, ASP.NET Core, RabbitMQ client |
| `Chat.Infrastructure` | EF Core `ChatDbContext` + configurations, Identity stores, repositories, RabbitMQ connection/publisher/consumer, Stooq typed `HttpClient` | Business rules |
| `Chat.Web` | `ChatHub`, Razor pages, `Program.cs` composition root, SignalR notifier adapter | Business rules, direct EF queries |
| `Chat.Bot` | `Program.cs`, hosted consumer service | Business rules, EF Core, SignalR |

Enforcement: `Chat.Domain.csproj` has zero `PackageReference`/`ProjectReference` items; a reviewer can
verify the rule in five seconds. `dotnet list <project> reference` is part of the review checklist.

DI is grouped per layer in extension methods: `AddApplication()` (Application),
`AddPersistence()` / `AddMessaging()` / `AddStockQuotes()` (Infrastructure). Hosts opt in to only what
they need — `Chat.Bot` never calls `AddPersistence()`, so it physically cannot write to the database.

---

## 2. Domain model

### 2.1 Aggregates

**`ChatRoom`** (aggregate root, `Chat.Domain.ChatRooms`)

| Member | Notes |
| --- | --- |
| `ChatRoomId Id` | strongly-typed `readonly record struct` over `Guid` |
| `RoomName Name` | value object, unique (DB unique index + pre-check in the handler) |
| `DateTimeOffset CreatedAtUtc`, `string CreatedByUserId` | audit |
| `static Result<ChatRoom> Create(RoomName name, string createdByUserId, DateTimeOffset nowUtc)` | raises `ChatRoomCreated` |

**`Message`** (aggregate root, `Chat.Domain.Messages`)

| Member | Notes |
| --- | --- |
| `MessageId Id` | strongly-typed id |
| `ChatRoomId ChatRoomId` | reference by **id**, never a navigation to `ChatRoom` |
| `MessageAuthor Author` | value object `(UserId, DisplayName)` |
| `MessageContent Content` | value object |
| `DateTimeOffset PostedAtUtc` | ordering key |
| `MessageOrigin Origin` | `Participant` or `Bot` |
| `static Result<Message> PostByParticipant(ChatRoomId, MessageAuthor, MessageContent, DateTimeOffset)` | raises `MessagePosted` |
| `static Result<Message> PostByBot(ChatRoomId, MessageContent, DateTimeOffset)` | author fixed to the system bot |

`Message` is its own aggregate rather than a child collection of `ChatRoom`. Rationale: the only write is
"append one message" and the only read is "last 50 of a room". Making messages a collection on `ChatRoom`
would force loading the room's history to post a single line — the exact resource problem the challenge
warns about.

### 2.2 Value objects

All are immutable, validated in a `static Create(...)` returning `Result<T>` — invalid state is
unrepresentable.

| Value object | Invariants |
| --- | --- |
| `MessageContent` | trimmed, non-empty, `<= MessageConstants.MaxContentLength` (500) |
| `StockCode` | trimmed, lower-cased, matches `^[a-z0-9.\-]{1,20}$`; `Display` returns upper case (`AAPL.US`) for the bot's wording |
| `RoomName` | trimmed, non-empty, `<= 60`, collapsed whitespace |
| `MessageAuthor` | non-empty `UserId` and `DisplayName`; `MessageAuthor.Bot` is the well-known singleton |
| `ChatRoomId` / `MessageId` | `readonly record struct` wrappers over `Guid`, `New()` factory, EF value converter |

`StockCode` validating **before** the URL is built is the anti-injection control for the outbound Stooq call.

### 2.3 Parsing the chat input (domain service)

`ChatCommandParser.Parse(string rawInput) -> ParsedChatInput`

`ParsedChatInput` is a closed hierarchy:

- `ParsedChatInput.PlainMessage(MessageContent content)`
- `ParsedChatInput.StockQuote(StockCode code)`
- `ParsedChatInput.UnknownCommand(string commandName)`
- `ParsedChatInput.Invalid(Error error)` — e.g. `/stock=` with an empty or malformed code

Recognition rules: the input must start with `/` (after trimming) to be a command; the command name is
compared case-insensitively (`/STOCK=AAPL.US` works); everything after the first `=` is the argument.
Anything not starting with `/` is a plain message and is never inspected further.

This lives in the Domain because "`/stock=` is a command, not a post" is a business rule, and it is the
single highest-value unit-test target in the repo.

### 2.4 Domain events

| Event | Raised by | Consumed by |
| --- | --- | --- |
| `ChatRoomCreated(ChatRoomId, RoomName, DateTimeOffset)` | `ChatRoom.Create` | logging / future |
| `MessagePosted(MessageId, ChatRoomId, MessageAuthor, MessageContent, DateTimeOffset)` | `Message.PostBy*` | application handler that broadcasts through `IChatNotifier` |

`IDomainEvent` is framework-free (no `MediatR.INotification` in the Domain). `AggregateRoot<TId>` records
events; the Application dispatches them **after** the unit of work commits, so a failed save never produces
a phantom broadcast.

`StockCommandReceived` is deliberately **not** a domain event: no aggregate is created or mutated when a
stock command is typed, so there is nothing to raise it from. The command is recognised by the parser and
turned directly into `RequestStockQuoteCommand`. (This is the one place where I deviate from the
`ddd-patterns` skill's suggestion; raising an event from nothing would have needed a fake aggregate.)

### 2.5 Repository interfaces (declared in `Chat.Application/Abstractions/Persistence`)

```
IChatRoomRepository   ExistsAsync(ChatRoomId, ct)
                      GetByNameAsync(RoomName, ct)
                      ListAsync(ct)                     -> IReadOnlyList<ChatRoomSummaryDto>
                      Add(ChatRoom room)

IMessageRepository    Add(Message message)
                      GetLatestAsync(ChatRoomId, int count, ct) -> IReadOnlyList<MessageDto>

IUnitOfWork           SaveChangesAsync(ct)
```

Read methods return DTOs, not entities: the read path is a projection, the write path uses aggregates.
Placing these in Application (rather than Domain) follows the `clean-architecture` skill, which names
`IMessageRepository` as an Application abstraction.

Other Application abstractions:

| Interface | Implemented in | Purpose |
| --- | --- | --- |
| `IStockQuoteRequester` | Infrastructure (RabbitMQ publisher) | Web -> broker |
| `IStockQuoteResponder` | Infrastructure (RabbitMQ publisher) | Bot -> broker |
| `IStockQuoteProvider` | Infrastructure (Stooq typed client) | Bot -> Stooq |
| `IChatNotifier` | **Chat.Web** (SignalR adapter) | broadcast to a room group |
| `IDateTimeProvider` | Infrastructure | testable clock |

`IChatNotifier` is implemented in `Chat.Web`, not Infrastructure, because SignalR's `IHubContext` is an
ASP.NET Core concern. That is allowed: the composition root wires it, and Application still only sees the
interface.

---

## 3. Application layer

### 3.1 Use cases

| Feature folder | Request | Returns | Notes |
| --- | --- | --- | --- |
| `Features/Messages/PostMessage` | `PostMessageCommand(ChatRoomId, RawInput, AuthorUserId, AuthorDisplayName)` | `Result<PostMessageResponse>` | The branch point of the whole challenge (see §5) |
| `Features/Messages/GetLatestMessages` | `GetLatestMessagesQuery(ChatRoomId, Count = 50)` | `Result<IReadOnlyList<MessageDto>>` | |
| `Features/Messages/PostBotMessage` | `PostBotMessageCommand(ChatRoomId, Text)` | `Result` | Invoked by the Web-side broker consumer |
| `Features/StockCommands/RequestStockQuote` | `RequestStockQuoteCommand(ChatRoomId, StockCode, RequestedByUserId, RequestedByDisplayName)` | `Result` | Publishes to RabbitMQ. **No repository dependency at all** |
| `Features/StockCommands/ResolveStockQuote` | `ResolveStockQuoteCommand(StockQuoteRequested)` | `Result` | Runs **inside Chat.Bot**: Stooq lookup + format + publish response |
| `Features/Rooms/CreateRoom` | `CreateRoomCommand(Name, CreatedByUserId)` | `Result<Guid>` | Bonus: multiple rooms |
| `Features/Rooms/ListRooms` | `ListRoomsQuery()` | `Result<IReadOnlyList<ChatRoomSummaryDto>>` | Bonus |

`AuthorUserId` / `AuthorDisplayName` are always filled by the hub from `Context.User` claims. The client
payload contains only the room id and the raw text. See §7.

### 3.2 Pipeline

MediatR `IPipelineBehavior` chain, registered in this order:

1. `LoggingBehavior<,>` — `Debug` on entry, `Warning` on a failed `Result` (never `Information` per message: chat is high volume).
2. `ValidationBehavior<,>` — runs all `IValidator<TRequest>` and converts failures into a failed `Result` via reflection over `Result.Failure<T>`. Validation never throws.

Both behaviors are constrained to `where TResponse : Result`, which is why every request implements
`ICommand`/`ICommand<T>`/`IQuery<T>` (markers over `IRequest<Result>` / `IRequest<Result<T>>`).

### 3.3 Result pattern

`Result` / `Result<T>` + `Error(Code, Message)` live in `Chat.Domain.Common` so value-object factories can
return them. Expected failures (empty message, unknown room, malformed stock code, broker unavailable) are
`Result` failures. Exceptions are reserved for programming errors and unrecoverable infrastructure faults.
Mapping at the edge: the hub returns the `Error` to the caller only (`Clients.Caller.ReceiveError`), never
to the whole room.

### 3.4 Messaging contracts — where they live

**Decision: the wire contracts live in `Chat.Application/Contracts/Messaging`, not in a separate
`Chat.Contracts` project.**

- Both hosts already reference `Chat.Application` transitively, so no new edge is introduced and the
  `clean-architecture` skill's "share via project reference or a small Contracts project" is satisfied.
- `Chat.Application` has no EF/ASP.NET/RabbitMQ dependency, so the contracts stay transport-agnostic.
- Trade-off: contract and use-case code are versioned together. If Web and Bot were released
  independently (different teams, different cadence) I would extract `Chat.Contracts` as a NuGet package
  with its own semver. For a single-repo, single-release deliverable that is ceremony without benefit.

Types: `StockQuoteRequested`, `StockQuoteResolved`, `StockQuoteOutcome`, `MessagingConstants`.

---

## 4. Messaging topology

All names are declared once, in `Chat.Application/Contracts/Messaging/MessagingConstants.cs`.

```
                       exchange: chat.stock (direct, durable)
Chat.Web ──publish──▶ [rk: stock.quote.request ] ──▶ queue: stock.quote.requests  ──consume──▶ Chat.Bot
Chat.Web ◀──consume── queue: stock.quote.responses ◀── [rk: stock.quote.response] ◀──publish── Chat.Bot

                       exchange: chat.stock.dlx (direct, durable)
                       ├─ stock.quote.requests.dlq
                       └─ stock.quote.responses.dlq
```

| Setting | Value | Rationale |
| --- | --- | --- |
| Exchange type | `direct` | Two routing keys, no wildcards needed. `topic` would be speculative generality |
| Exchange/queue durability | durable, non-exclusive, non-auto-delete | Survives a broker restart during a demo |
| Message persistence | `Persistent = true` | A quote request must not vanish if the broker restarts |
| Prefetch (QoS) | `10` per consumer channel (`MessagingConstants.PrefetchCount`) | Bounded in-flight work; work is I/O-bound and cheap to redo |
| Ack mode | manual `BasicAckAsync` after successful handling | No message loss on crash |
| Failure handling | `BasicNackAsync(requeue: false)` -> dead-letter | Poison messages go to the DLQ instead of spinning forever (an infinite requeue loop is exactly "consuming too many resources") |
| Retry | Transient Stooq faults are retried **inside** the bot by the resilience handler, not by requeueing | Keeps broker traffic flat |
| Serialization | `System.Text.Json`, camelCase, enums as strings, `ContentType = application/json` | Readable in the RabbitMQ management UI, forward-compatible |
| Connection | **one** `IConnection` per process (singleton), one `IChannel` per consumer/publisher | The performance skill's hard rule; never connect per message |
| Topology declaration | idempotent `ExchangeDeclare`/`QueueDeclare`/`QueueBind` on startup, by both processes | Either process can be started first |
| Startup resilience | connection factory with `AutomaticRecoveryEnabled` + bounded retry loop on first connect | RabbitMQ container is often not ready yet locally |

### Scale-out note (documented, not implemented)

`stock.quote.responses` is a single shared queue. With more than one `Chat.Web` instance, only the instance
that consumed the message would broadcast, and users connected to the other instance would see nothing.
The correct fix is a fanout exchange with a per-instance exclusive auto-delete queue **plus** a SignalR
backplane (Redis / Azure SignalR). Out of scope for a single-instance deliverable, but called out so the
reviewer knows it is a conscious decision rather than an oversight.

---

## 5. End-to-end stock flow (and where "never persist" is enforced)

```
Browser                Chat.Web                        RabbitMQ                 Chat.Bot            Stooq
   │  SendMessage(roomId, "/stock=aapl.us")
   ├──────────────────────▶ ChatHub.SendMessage
   │                        (author from Context.User claims)
   │                        │ PostMessageCommand
   │                        ▼
   │                    PostMessageHandler
   │                        │ ChatCommandParser.Parse(raw)
   │                        │
   │            ┌───────────┴────────────┐
   │            │ PlainMessage           │ StockQuote(code)
   │            ▼                        ▼
   │      Message.PostByParticipant   RequestStockQuoteCommand
   │      repo.Add + SaveChanges      (NO repository injected)
   │      IChatNotifier.Broadcast          │ IStockQuoteRequester.RequestAsync
   │                                       ├──── stock.quote.request ────▶ stock.quote.requests
   │                                                                              │
   │                                                                    ResolveStockQuoteHandler
   │                                                                              ├── GET /q/l/?s=aapl.us&f=sd2t2ohlcv&h&e=csv ──▶
   │                                                                              │◀── Symbol,Date,Time,Open,High,Low,Close,Volume
   │                                                                              │    AAPL.US,2026-08-03,21:00:00,...,93.42,...
   │                                                                    format "AAPL.US quote is $93.42 per share"
   │                                       ◀──── stock.quote.response ─── IStockQuoteResponder
   │                        StockQuoteResponseConsumer (Chat.Web)
   │                        │ PostBotMessageCommand
   │                        ▼
   │                    Message.PostByBot -> repo.Add + SaveChanges
   │  ◀── ReceiveMessage ── IChatNotifier.BroadcastAsync(roomId, botMessage)
```

**The "never persist the stock command" rule is enforced in exactly one place:**
`PostMessageHandler` branches on the parser result and, on the `StockQuote` branch, returns without ever
touching `IMessageRepository`. Structurally reinforced by `RequestStockQuoteHandler` having **no**
repository or `IUnitOfWork` dependency in its constructor — it is not possible for it to write a row.

Unit test that locks this down: `Handle_StockCommand_DoesNotPersistMessage` asserts
`repository.DidNotReceive().Add(Arg.Any<Message>())` **and** `requester.Received(1).RequestAsync(...)`.

### Bot message persistence — explicit decision

Bot answers **are** persisted (`MessageOrigin.Bot`, author = the well-known bot participant) and then
broadcast. Rationale: the challenge says "the post owner should be the bot", i.e. it is a post; and a
reviewer who refreshes the page after a `/stock=` would otherwise see the quote disappear, which reads as a
bug. The hard constraint only forbids persisting the **command**, not the answer. This overrides the
"broadcast-only" default in the `ddd-patterns` skill and is repeated in the README design-decisions section.

### Unknown / malformed commands

- `/help`, `/stock`, `/stock=` -> `UnknownCommand` / `Invalid`: nothing persisted, nothing published; the
  hub sends a private hint back to the caller only.
- Unknown ticker (Stooq returns `N/D`) -> the bot publishes `StockQuoteOutcome.SymbolNotFound` with the
  message `"Sorry, I could not find a quote for AAPL.XX."`.
- Stooq unreachable / unparseable -> `StockQuoteOutcome.LookupFailed` with a friendly message. The bot
  never crashes and never dead-letters for these — they are answers, not failures.

---

## 6. Persistence

**Decision: EF Core 10 + SQL Server 2022**, running in Docker alongside RabbitMQ
(`docker-compose.dev.yml`). Database `ChatDb`, created by `dotnet ef database update`.

| Option | Verdict |
| --- | --- |
| **SQL Server 2022 in Docker** | Chosen. The production-realistic target for a .NET stack, and the reviewer already has to run a container for RabbitMQ — one `docker compose up` now starts everything. Real concurrency, real index statistics, real relational semantics |
| SQLite | Rejected: a single-writer file database that would not exercise the concurrency the chat actually has, and would leave the deliverable one provider swap away from the environment it is meant to run in |
| EF In-Memory provider | Rejected: no relational semantics, no real index behaviour, would make the "last 50" query design meaningless |

Trade-offs accepted:
- ~1.5 GB image and ~30 s first start. Mitigated by the healthcheck plus a bounded startup retry in the
  host, so `dotnet run` does not race the container.
- Docker becomes a hard prerequisite for the database as well as the broker. It already was for the broker.
- On Apple Silicon `mcr.microsoft.com/mssql/server:2022-latest` runs under emulation; swap the image for
  `mcr.microsoft.com/azure-sql-edge` if that matters. Documented in the README.

The mapping and queries use no provider-specific SQL, so the provider remains a one-line change in
`AddPersistence`.

Identity uses the **same** `DbContext` (`ChatDbContext : IdentityDbContext<ApplicationUser>`) — one
connection pool, one migration history, one transaction scope.

### The "last 50 ordered by timestamp" query

```csharp
List<MessageDto> latest = await _context.Messages
    .AsNoTracking()
    .Where(m => m.ChatRoomId == roomId)
    .OrderByDescending(m => m.PostedAtUtc)
    .ThenByDescending(m => m.Id)                       // deterministic tie-break
    .Take(count)                                       // MessageConstants.LatestMessagesCount = 50
    .Select(m => new MessageDto(m.Id.Value, m.Author.DisplayName, m.Content.Value, m.PostedAtUtc, m.Origin))
    .ToListAsync(cancellationToken);

latest.Reverse();                                      // oldest -> newest for display
```

- Composite index `IX_Messages_ChatRoomId_PostedAtUtc` on `(ChatRoomId, PostedAtUtc DESC)` — the query is
  a single index range scan, cost independent of history size.
- `AsNoTracking()` + projection: no change tracker entries, no entity materialisation.
- `Take(50)` runs in the database. The full history is **never** loaded or broadcast.
- The reverse happens in memory on 50 rows — cheaper and clearer than a subquery.
- `PostedAtUtc` is stored as `datetime2(7)` and always written in UTC; the `DateTimeKind` is restored on
  read by a value converter so no local-time drift can enter the ordering.

---

## 7. Authentication, authorization and SignalR

- **ASP.NET Core Identity** with the default UI (`Microsoft.AspNetCore.Identity.UI`), cookie auth,
  `ApplicationUser : IdentityUser` adding `DisplayName`. Bonus feature, and it is the cheapest correct way
  to satisfy the mandatory "registered users log in".
- Default password policy and lockout are kept as-is.
- `[Authorize]` on `ChatHub` and on the chat page. Anonymous hub connections are rejected by the framework.
- **Identity always comes from `Context.User`:**
  `string userId = Context.UserIdentifier!;` and the display name from a claim. The client payload carries
  only `roomId` and `text`. A client cannot impersonate another participant or the bot.
- SignalR **groups per room**: `Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(roomId))` on join,
  `Clients.Group(GroupFor(roomId)).ReceiveMessage(dto)` on broadcast. Never `Clients.All` — required for
  the multiple-rooms bonus and for not spamming uninvolved connections.
- Hub methods stay ~5 lines: read claims, send the MediatR request, map the `Result`.
- Message text is rendered with `textContent` in the browser, never `innerHTML` — XSS is closed at output.
- A per-user posting throttle guards against flooding (security **and** resource concern).

---

## 8. Resource consumption

| Area | Control |
| --- | --- |
| RabbitMQ | One `IConnection` per process (singleton), one `IChannel` per publisher/consumer, prefetch 10, manual ack, DLQ instead of requeue loops |
| HTTP | `IHttpClientFactory` typed `StooqClient` with `BaseAddress`, 10 s timeout and a standard resilience handler (retry with jittered backoff + circuit breaker). No `new HttpClient()` |
| EF Core | `AsNoTracking()` + projection on every read, single indexed `Take(50)` query, no lazy loading, no navigation properties between aggregates (so no N+1 is even expressible) |
| SignalR | Group-scoped broadcasts, small DTO payloads, no per-connection server state beyond group membership, cleanup in `OnDisconnectedAsync` |
| Logging | Per-message logging at `Debug`; `Information` reserved for lifecycle events |
| Startup | Migrations applied once at startup, not per request |

---

## 9. Health endpoints

Both hosts expose the same three routes, mapped from one definition
(`Chat.Infrastructure/HealthChecks/HealthCheckEndpoints.cs`) so they cannot drift apart.

| Route | Contents | Semantics |
| --- | --- | --- |
| `/health` | every registered dependency | full JSON report; 200 healthy/degraded, 503 unhealthy |
| `/health/ready` | checks tagged `ready` | can this process do useful work yet |
| `/health/live` | no dependency probe at all | is this process running |

| Host | Dependencies probed |
| --- | --- |
| `Chat.Web` | `sql-server` (`SELECT 1`), `rabbitmq` (open a connection) |
| `Chat.Bot` | `rabbitmq`, `stooq` (probe the service root) |

Design decisions:

- **Liveness never probes a dependency.** If `/health/live` failed when RabbitMQ went down, an
  orchestrator would restart a perfectly healthy process and lose the broker's automatic recovery.
- **Stooq is tagged `external` and reports `Degraded`, not `Unhealthy`**, and is excluded from
  `/health/ready`. A third-party outage is not the bot's fault: the bot stays ready and answers
  "could not look that up right now", which is exactly the graceful-degradation bonus.
- **The bot has no database probe**, because it has no database. That absence is the visible proof of
  the decoupling the challenge asks for.
- **Chat.Bot is a `Microsoft.NET.Sdk.Web` host purely to serve these probes.** It maps no chat routes
  and still never references `Chat.Web`.
- **The health probe does not fetch a real quote.** It probes the Stooq root, so monitoring cannot
  consume the rate budget the actual feature depends on.
- The JSON payload reports an exception's **message only, never its stack trace** — a stack trace would
  leak server names and file paths to anyone who can reach the endpoint.
- A missing connection string is *reported* as unhealthy with an actionable message rather than thrown
  at startup, so a misconfigured host explains itself instead of crash-looping.

Written by hand rather than pulled from `AspNetCore.HealthChecks.*`: the three probes are ~30 lines
each, `SqlClient`/`RabbitMQ.Client`/`HttpClient` are already in the dependency graph, and it avoids
another third-party licence and version to track.

---

## 10. Trade-offs and out of scope

**Deliberate trade-offs**

| Decision | Alternative | Why |
| --- | --- | --- |
| MediatR pinned to 12.5.0 | 14.x | 13.0 changed to a commercial licence; 12.x is the last Apache-2.0 release and is feature-complete for this use |
| FluentAssertions pinned to 7.2.2 | 8.x | 8.0 moved to the Xceed commercial licence |
| SQL Server 2022 in Docker | SQLite file database | Production-realistic for a .NET stack and a real concurrency model; the reviewer already needs Docker for RabbitMQ, so one compose file starts the whole environment |
| Integration tests on Testcontainers | shared dev container / in-memory provider | Same provider as production, per-run isolation, no state leaking between runs |
| Contracts inside `Chat.Application` | separate `Chat.Contracts` project | No independent versioning need; avoids an eighth project |
| Bot answers persisted | broadcast-only | "The post owner should be the bot" implies a post; survives a page refresh |
| Razor Pages + Identity default UI + vanilla JS | SPA | The challenge explicitly says frontend as simple as possible |
| Classic `.sln` | `.slnx` | Broadest tool compatibility for whoever opens the deliverable |
| Hand-written health checks | `AspNetCore.HealthChecks.*` packages | ~30 lines each against clients already in the graph; no extra licence, version or advisory surface |
| `FrameworkReference Microsoft.AspNetCore.App` in Infrastructure | duplicating the endpoint mapping in both hosts | One definition of the health routes and payload; the dependency rule is about direction, and nothing here is visible to Application or Domain |
| `InvariantGlobalization=false` | the template default `true` | `Microsoft.Data.SqlClient` refuses to connect under invariant mode; parsing still pins `InvariantCulture` explicitly |

**Out of scope (intentionally)**

- Horizontal scale-out of `Chat.Web` (needs a SignalR backplane and per-instance response queues — §4).
- Message editing/deletion, read receipts, typing indicators, attachments, presence lists.
- Quote caching or rate-limiting towards Stooq beyond the HTTP resilience policy.
- Distributed tracing/OpenTelemetry; structured logging to the console is enough here.
- Saga/outbox between the DB write and the SignalR broadcast. Documented as a known at-most-once edge:
  if the process dies between commit and broadcast, connected clients see the message on their next reload.

---

## 10. Repository map

```
Chat.sln
Directory.Build.props          net10.0, nullable, implicit usings, warnings-as-errors, LangVersion latest
Directory.Packages.props       central package management (all versions pinned here)
global.json                    SDK 10.0.100 + rollForward latestFeature
.editorconfig                  clean-code conventions, enforced on build
docker-compose.yml             RabbitMQ 4 + management UI
.env.example                   local broker credentials template (.env is git-ignored)
docs/ARCHITECTURE.md           this file
docs/PLAN.md                   ordered, committable task list

src/Chat.Domain/
  Common/                      Result, Error, Entity, AggregateRoot, IDomainEvent
  ChatRooms/                   ChatRoom, RoomName, ChatRoomId, ChatRoomCreated
  Messages/                    Message, MessageContent, MessageAuthor, MessageId, MessageOrigin,
                               MessagePosted, MessageConstants
  StockCommands/               StockCode, ChatCommandParser, ParsedChatInput

src/Chat.Application/
  Abstractions/Messaging/      ICommand, ICommandHandler, IQuery, IQueryHandler
  Abstractions/Persistence/    IChatRoomRepository, IMessageRepository, IUnitOfWork
  Abstractions/Realtime/       IChatNotifier
  Abstractions/Stocks/         IStockQuoteProvider
  Abstractions/Time/           IDateTimeProvider
  Behaviors/                   LoggingBehavior, ValidationBehavior
  Contracts/Messaging/         MessagingConstants, StockQuoteRequested, StockQuoteResolved
  Features/                    Messages/, Rooms/, StockCommands/
  DependencyInjection.cs       AddApplication()

src/Chat.Infrastructure/
  Persistence/                 ChatDbContext, configurations, repositories, migrations
  Identity/                    ApplicationUser, Identity configuration
  Messaging/                   RabbitMqConnection, publishers, consumer base, RabbitMqOptions
  Stocks/                      StooqClient, StooqCsvParser, StooqOptions
  DependencyInjection.cs       AddPersistence(), AddMessaging(), AddStockQuotes()

src/Chat.Web/                  Program.cs, Hubs/ChatHub.cs, Realtime/SignalRChatNotifier.cs,
                               Messaging/StockQuoteResponseConsumer.cs, Pages/, Areas/Identity/
src/Chat.Bot/                  Program.cs, StockQuoteRequestConsumer.cs

tests/Chat.UnitTests/          mirrors src structure
tests/Chat.IntegrationTests/   CustomWebApplicationFactory + flow tests
```
