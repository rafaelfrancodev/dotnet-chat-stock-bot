# Financial Chat — .NET Chat + Stock Bot

A browser chat application where registered users log in and talk in a chatroom over SignalR. Typing
`/stock=aapl.us` is not a chat message: it is a command that is published to RabbitMQ, picked up by a
**separate process** (`Chat.Bot`) that has no database access at all, resolved against Stooq's CSV quote
endpoint, and published back so the web host posts the answer into the room as the **bot**. The command
itself is never written to the database — only the bot's answer is.

## Mandatory requirements and where they are

| Requirement | Where |
| --- | --- |
| Registered users log in and chat in a room | ASP.NET Core Identity (`src/Chat.Web/Areas/Identity`), `ChatHub` (`src/Chat.Web/Hubs/ChatHub.cs`) |
| `/stock=stock_code` command | `ChatCommandParser` + `StockCode` (`src/Chat.Domain/StockCommands/`) |
| Decoupled bot over RabbitMQ | `src/Chat.Bot` (no `AddPersistence()`, no SignalR, no reference to `Chat.Web`) |
| Stooq CSV call and `"AAPL.US quote is $93.42 per share"` | `StooqClient` / `StooqCsvParser` (`src/Chat.Infrastructure/Stocks/`), `StockQuoteAnswer` |
| Post owner is the bot | `Message.PostByBot` — takes no author, uses `MessageAuthor.Bot` |
| Ordered by timestamp, last 50 only | `MessageRepository.GetLatestAsync`, capped in `GetLatestMessagesValidator` |
| The stock command is never persisted | Enforced structurally in four layers — see [Design decisions](#design-decisions) |
| Unit tests | 503 tests in `tests/Chat.UnitTests` |

---

## Prerequisites

| Tool | Version | Notes |
| --- | --- | --- |
| .NET SDK | **10.0.100** or later (`global.json`, `rollForward: latestFeature`) | verified on 10.0.302 |
| Docker Desktop | any current release | runs SQL Server 2022 and RabbitMQ 4 |
| PowerShell 7+ (`pwsh`) | optional | only for `scripts/run-dev.ps1` |

**Visual Studio 2022 cannot open this solution.** Its MSBuild 17.x cannot load the .NET 10 SDK, which
ships MSBuild 18.6. Use **Visual Studio 2026**, VS Code, or Rider — or just the CLI, which is what every
command below uses.

---

## Getting started

Run these from the repository root, in order.

**1. Local credentials.** `.env` is git-ignored and holds only local container credentials.

```bash
cp .env.example .env          # PowerShell: Copy-Item .env.example .env
```

Then edit `.env` and set `MSSQL_SA_PASSWORD` to something that satisfies SQL Server's complexity policy
(8+ characters with upper case, lower case, digit and symbol) and pick a `RABBITMQ_PASSWORD`. The SA
password is baked into the SQL Server volume on first start; changing it later needs
`docker compose -f docker-compose.dev.yml down -v`.

**2. Restore the local tools** (`dotnet-ef` 10.0.10 and `libman`, pinned in `.config/dotnet-tools.json`).

```bash
dotnet tool restore
```

**3. Start the infrastructure** and wait until both containers report `(healthy)`.

```bash
docker compose -f docker-compose.dev.yml up -d
docker compose -f docker-compose.dev.yml ps
```

Do not continue until `chat-sqlserver` and `chat-rabbitmq` both show `(healthy)`. SQL Server needs roughly
30 seconds on a first start; the compose healthcheck covers it.

**4. Give the two hosts their secrets.** `appsettings.json` deliberately ships **no** credentials: the
connection string and the broker user/password are empty placeholders. Supply them through user-secrets
(below) or through the `ConnectionStrings__ChatDatabase` / `RabbitMq__UserName` / `RabbitMq__Password`
environment variables — `.env.example` has commented-out lines for the environment-variable route.

Use `127.0.0.1`, **never** `localhost`. The containers publish on the IPv4 loopback only, and on Windows
`localhost` resolves to `::1` first, which costs a full 15-second SqlClient timeout before it falls back.

```bash
dotnet user-secrets set "ConnectionStrings:ChatDatabase" "Server=127.0.0.1,1433;Database=ChatDb;User Id=sa;Password=<MSSQL_SA_PASSWORD from .env>;Encrypt=True;TrustServerCertificate=True" --project src/Chat.Web
dotnet user-secrets set "RabbitMq:UserName" "<RABBITMQ_USER from .env>" --project src/Chat.Web
dotnet user-secrets set "RabbitMq:Password" "<RABBITMQ_PASSWORD from .env>" --project src/Chat.Web
dotnet user-secrets set "RabbitMq:UserName" "<RABBITMQ_USER from .env>" --project src/Chat.Bot
dotnet user-secrets set "RabbitMq:Password" "<RABBITMQ_PASSWORD from .env>" --project src/Chat.Bot
```

`TrustServerCertificate=True` is required locally: the container uses a self-signed certificate and
Microsoft.Data.SqlClient encrypts by default. `Chat.Bot` gets broker credentials only — it has no
connection string because it has no database.

**5. Build.**

```bash
dotnet build          # expect: 0 Warning(s), 0 Error(s)
```

**No migration step is needed.** `Chat.Web` applies pending migrations and seeds the default `General`
room on startup, before the first request, and the operation is idempotent (it logs
`Chat database schema is up to date; no migration applied.` when there is nothing to do). If you prefer
to do it by hand — or want to inspect the schema before running the app — the manual equivalent is:

```bash
dotnet ef migrations list    -p src/Chat.Infrastructure -s src/Chat.Web   # 20260804232200_InitialCreate
dotnet ef database update    -p src/Chat.Infrastructure -s src/Chat.Web
```

---

## Running it

The stock flow needs **both** processes. Pick one of these.

```bash
pwsh ./scripts/run-dev.ps1              # builds, starts both, waits for /health/live, Ctrl+C stops both
pwsh ./scripts/run-dev.ps1 -SkipBuild   # skip the build
```

Or two terminals:

```bash
dotnet run --project src/Chat.Web       # terminal 1
dotnet run --project src/Chat.Bot       # terminal 2
```

Or, in Visual Studio 2026, choose the **"Chat.Web + Chat.Bot"** startup profile (`Chat.slnLaunch`)
instead of a single startup project. That profile uses Chat.Web's `https` launch profile, so the browser
opens on `https://localhost:7204` (with `http://localhost:5271` still listening).

| Surface | URL |
| --- | --- |
| Chat application | http://localhost:5271 |
| Chat.Web health | http://localhost:5271/health |
| Chat.Bot health | http://localhost:5299/health |
| RabbitMQ management UI | http://localhost:15672 (credentials from `.env`) |

`Chat.Bot` serves health probes only — it has no chat surface.

---

## Trying the features

This is the script the challenge says a reviewer will follow, and it works as written.

1. Open http://localhost:5271 in **two separate browser windows** (use two profiles, or one normal and
   one private window, so the two auth cookies do not overwrite each other).
2. In each window, click **Register** and create a user. Registration asks for a **display name** as well
   as email and password; that name is what every message is labelled with. Registration signs you in and
   redirects straight to `/Chat`. (Logging in later lands on the home page — use the **Chat** link in the
   nav bar.)
3. Type a line in one window. It appears in **both**, once, immediately, oldest-first, with the sender's
   display name. Only the last 50 messages of the room are ever loaded.
4. In one window, type `/stock=aapl.us`.
   - The command line itself never appears in the chat and is never stored.
   - A message from **`Bot`** appears in **both** windows.
   - With Stooq's CSV endpoint reachable, that message reads `AAPL.US quote is $93.42 per share`.
     As measured today it is not reachable, so the bot answers
     `I could not reach the quote service, so I have no price for AAPL.US right now.` and the window
     that asked also gets a red banner. See [A note about Stooq](#a-note-about-stooq).
5. Type an unknown command such as `/help`. Only the window that typed it sees an error
   (`Unknown command. The only command available is /stock=<code>, for example /stock=aapl.us.`); the
   room sees nothing and nothing is stored.
6. Refresh either window. The chat history — including the bot's answer, but not the command — reloads
   in timestamp order.

Everything above was exercised end-to-end against the running stack on 2026-08-05 with two authenticated
users in the seeded `General` room. `SELECT COUNT(*) FROM Messages WHERE Content LIKE '/%'` returned
**0** afterwards, while the bot's answer was present as a row owned by `system:bot` with
`Origin = 2 (Bot)`.

---

## Health endpoints

Both hosts expose the same three routes.

| Route | Behaviour |
| --- | --- |
| `/health` | every registered check, as JSON; 200 healthy / 503 unhealthy |
| `/health/ready` | dependencies tagged `ready` only — the ones that must work to serve a request |
| `/health/live` | process liveness; runs no dependency probe |

```bash
curl http://localhost:5271/health    # masstransit-bus, sql-server, rabbitmq
curl http://localhost:5299/health    # masstransit-bus, rabbitmq, stooq
```

The Stooq check is tagged `external` and is deliberately **excluded from `/health/ready`**: a third-party
outage must not mark the bot unready and get it restarted or pulled out of rotation, because the bot's own
job — consuming requests and answering politely — still works.

---

## Tests

```bash
dotnet test
```

**503 tests, all passing.** The suite is hermetic: it needs no containers, no broker and no network
access. Database behaviour is covered by translating EF Core queries offline (`ToQueryString()`) and by
SQLite in process memory where a real unique index is needed; messaging is covered by MassTransit's
in-memory `ITestHarness`; Stooq is covered by a stubbed `HttpMessageHandler` pointed at a
deliberately-unroutable host so a bypassed stub fails loudly instead of calling the real service.

`dotnet test` also prints `No test is available in ... Chat.IntegrationTests.dll`. That is expected —
the integration suite (task 1.17) is not written yet, and the exit code is still 0.

Other gates:

```bash
dotnet build                       # 0 warnings (TreatWarningsAsErrors + EnforceCodeStyleInBuild)
dotnet format --verify-no-changes  # formatting gate
```

---

## Architecture

Two runnable processes, one solution, Clean Architecture with DDD tactical patterns and CQRS-style
handlers. Full design, including measurements and rejected alternatives, is in
**[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)**.

```
src/
  Chat.Domain/          entities, value objects, domain events, Result/Error — zero package references
  Chat.Application/     Features/<UseCase>/ command|query + handler + validator; ports for infrastructure
  Chat.Infrastructure/  EF Core + Identity, MassTransit/RabbitMQ, Stooq typed HttpClient, health checks
  Chat.Web/             SignalR hub, Identity UI, minimal Razor chat page, composition root
  Chat.Bot/             MassTransit consumer -> Stooq -> answer; health endpoints only
tests/
  Chat.UnitTests/       503 tests
  Chat.IntegrationTests/  empty (task 1.17)
```

Dependency rule — `Chat.Web`/`Chat.Bot` → `Chat.Infrastructure` → `Chat.Application` → `Chat.Domain`,
never the reverse. `Chat.Domain.csproj` has no package or project references at all, so the rule is
verifiable in seconds.

The stock flow:

```
Browser --SendMessage("/stock=aapl.us")--> ChatHub --> PostMessageCommand
    ChatCommandParser classifies the input
    plain message  -> persist + broadcast to the room's SignalR group
    /stock=<code>  -> RequestStockQuoteCommand -> publish StockQuoteRequested
                          |
                          v  RabbitMQ  stock-quote-requests
                      Chat.Bot: StockQuoteRequestConsumer -> ResolveStockQuoteHandler
                          -> GET stooq.com/q/l/?s=aapl.us&f=sd2t2ohlcv&h&e=csv -> parse Close column
                          -> publish StockQuoteResolved
                          |
                          v  RabbitMQ  stock-quote-responses
    Chat.Web: StockQuoteResponseConsumer -> PostBotMessageCommand
          -> Message.PostByBot -> persist -> broadcast to the room  (+ a private alert on failure)
```

MassTransit owns the exchange layout; the two receive endpoint names, the prefetch window and the retry
policy all live in `MessagingConstants`.

---

## Design decisions

**A `/stock=` command can never be persisted, and that is structural, not a convention.** Four
independent layers: the raw input is classified by `ChatCommandParser` before anything else and branched
on with a type `switch` over a closed hierarchy; the only method in `PostMessageHandler` that touches
`IMessageRepository`/`IUnitOfWork` takes a `PlainMessage` parameter, so persisting a command would mean
converting a `StockQuote` into a plain message, which the type system forbids; `RequestStockQuoteHandler`
— the whole `/stock=` path — is constructed with no persistence port at all, and a reflection test pins
that; and the `switch` ends in `UnreachableException`, so a new input kind fails loudly instead of falling
into the persist branch.

**Bot answers *are* persisted; the outage banner is not.** The challenge forbids persisting the command,
not the answer, and says the post owner should be the bot — so it is a post. A quote that vanished on
refresh would read as a bug. The transient half of the story is a `ChatAlert` delivered only to the
participant who asked, over `Clients.User(...)`; it is never stored and never appears in the last-50
history, because the post says what the bot answered while the banner says what the system is doing.

**SQL Server 2022 in Docker, not SQLite.** Identity, `datetime2` ordering semantics, `nvarchar` widths and
a real unique index differ enough that developing against SQLite would defer discovering those differences.
SQLite is a *test-only* dependency, used in process memory where a genuine unique index is needed.

**MassTransit 8.x, not the raw RabbitMQ client.** The raw client would mean hand-rolling connection
recovery, topology declaration, serialization, retry and dead-lettering. It also removes the need for a
`BackgroundService` in the bot: MassTransit's hosted bus *is* the worker — it owns the connection and the
prefetch window and pushes each message into an `IConsumer<T>`, so a polling loop beside it would duplicate
all of that and lose the measured retry/dead-letter behaviour.

**Wire contracts live in `Chat.Application/Contracts/Messaging`, not in a separate Contracts project.** Two
processes in one solution share them; a fifth project would add ceremony without adding a boundary. They
are plain records with no MassTransit types, so `Chat.Application` still references no messaging framework
— a unit test asserts the compiled assembly references no EF Core, MassTransit, ASP.NET Core or
RabbitMQ.Client.

**Licence pins, in `Directory.Packages.props`:** MediatR **12.x**, FluentAssertions **7.x**, MassTransit
**8.x**. Each next major version moved to a commercial licence, which this deliverable cannot accept. All
versions are centrally managed; no csproj carries one.

**`Messages` has no foreign keys.** The bot's author id is `system:bot`, which is not an Identity user, so
an FK to `AspNetUsers` would reject every quote answer the challenge requires. `ChatRoomId` is a
cross-aggregate reference validated with an `EXISTS` query, so an unknown room is an expected `Result`
failure instead of a `DbUpdateException`.

**Identity always comes from claims.** The hub fills the author from `Context.User` (`UserIdentifier` plus
the `display_name` claim); the client payload carries only the room id and the raw text.
`PostMessageCommand` has no author, origin or timestamp field, so no caller can post as somebody else, as
the bot, or at a chosen instant. Broadcasts always target a room group — `IChatNotifier` takes a
`ChatRoomId` on every member, so "send to all connections" is not expressible.

**Resource consumption.** The last-50 read is one projected `SELECT TOP(n)` of the five columns the window
renders, capped server-side so no client can widen it, served by `IX_Messages_ChatRoomId_PostedAtUtc`. No
per-connection server state is kept: a reconnect re-joins from the client rather than being restored from
an unbounded map.

---

## A note about Stooq

Measured from this machine on **2026-08-05**:

| Request | Result |
| --- | --- |
| `GET https://stooq.com/q/l/?s=aapl.us&f=sd2t2ohlcv&h&e=csv` | **404**, `text/html`, 271-byte "page does not exist" |
| the same URL with a desktop-browser `User-Agent` | **404**, `text/html` |
| the same path on `https://stooq.pl/` | **404**, `text/html` |
| `GET https://stooq.com/` | **200** |

The CSV endpoint the challenge specifies no longer exists. The site is up; that route is gone.

Consequence for a reviewer: `/stock=aapl.us` exercises the graceful-failure path rather than the quote
path. The room gets `I could not reach the quote service, so I have no price for AAPL.US right now.`
posted by `Bot`, and the participant who asked also gets a red banner
(`The stock quote service (Stooq) is not responding right now. Please try again in a couple of minutes.`).
Nothing crashes, nothing is dead-lettered, and `/health` stays 200 on both hosts.

The success path is fully implemented and unit-tested against the CSV format the challenge documents:
`StooqCsvParser` locates the price by **header name** rather than column position, `N/D` maps to
"symbol not found", and `StockQuoteAnswer.Quoted` produces exactly `AAPL.US quote is $93.42 per share`
(invariant culture, so a de-DE machine does not post `$93,42`). Both the endpoint and the query path are
configuration — `Stooq:BaseAddress` and `Stooq:QuotePath` in `src/Chat.Bot/appsettings.json` — so the
client can be repointed at a working endpoint without a code change.

---

## Bonus features

| Bonus | Status |
| --- | --- |
| .NET Identity authentication | **Done** |
| Bot handles unknown commands and exceptions gracefully | **Done** |
| Multiple chatrooms | **Not done** |
| Installer | **Not done** |

**.NET Identity authentication — done.** ASP.NET Core Identity over the same `ChatDbContext`, with
registration, login and logout from the default UI. The Register page is scaffolded to also capture a
required `DisplayName`, and a custom `IUserClaimsPrincipalFactory` carries it in the auth cookie as a
`display_name` claim — so every message is labelled with a real name and the hub needs no `AspNetUsers`
query per message. The hub carries `[Authorize]`; an anonymous `POST /hubs/chat/negotiate` is rejected
with 401 and `GET /Chat` redirects to the login page.

**Bot error handling — done.** Precisely:
- **Unknown commands never reach the bot.** `ChatCommandParser` classifies them in `Chat.Web`, and the
  hub answers the *caller only* over `ReceiveError`. Nothing is published, persisted or broadcast. The
  untrusted command name is never echoed back into the error text.
- **A malformed ticker never reaches Stooq.** `StockCode` enforces
  `^[a-z0-9.\-]{1,20}$`, so `&`, `?`, `/`, `=`, `%` and `#` cannot get into the URL; the bot's consumer
  re-validates the wire string through the same value object and acknowledges an unusable one instead of
  faulting the delivery.
- **An unknown ticker gets a friendly answer.** `N/D` in the CSV becomes
  `Sorry, I could not find a quote for AAPL.XX.` — a real answer, so no banner is raised.
- **Every Stooq failure mode maps to a polite message.** Non-success status, transport error, client
  timeout, open circuit breaker, oversized body, HTML error page and unparseable CSV all become
  `LookupFailed`, which becomes the "could not reach the quote service" line plus the private banner.
  `StooqClient` never throws except for the caller's own cancellation.
- **A poison message cannot spin forever.** Measured against the real broker: 4 delivery attempts about
  2 seconds apart (initial plus `RetryLimit = 3` at `RetryIntervalSeconds = 2`), after which the message
  sits in `<queue>_error` and the working queue is back to 0.

  Honest scope note: task 2.3 in [`docs/PLAN.md`](docs/PLAN.md) would add explicit hardening *tests* for
  malformed-payload dead-lettering and broker-restart recovery. The behaviour above is implemented and the
  retry/dead-letter numbers are measured, but those two extra tests are not written.

**Multiple chatrooms — not done** (bonus 2.2). The groundwork is in place and was built that way on
purpose: `ChatRoom` is a real aggregate with a unique room name, `Messages` are keyed by `ChatRoomId`,
`IChatNotifier` takes a `ChatRoomId` on every member and broadcasts to a per-room SignalR group, and
`Features/Rooms/` already holds `GetDefaultRoom`. What is missing is the create/list/switch use cases and
the UI to drive them; today the chat page always opens the seeded `General` room.

**Installer — not done** (bonus 2.5).

---

## What is not finished

Stated plainly, per the challenge's request to say which parts were completed. All **mandatory**
requirements are implemented and verified end-to-end. Outstanding items, tracked in
[`docs/PLAN.md`](docs/PLAN.md):

| Task | Item |
| --- | --- |
| 1.17 | Integration test suite (`Testcontainers.MsSql` + `WebApplicationFactory`); `Chat.IntegrationTests` is an empty project today |
| 1.18 | The manual end-to-end walkthrough recorded in `docs/ARCHITECTURE.md` |
| 2.2 | Multiple chatrooms (bonus) |
| 2.4 | Per-user posting rate limit (bonus) |
| 2.5 | Installer (bonus) |

No screenshot is included: the UI is deliberately minimal, since the challenge states the backend is what
is evaluated.
