# CLAUDE.md — Chat + Stock Bot Challenge (.NET)

Browser-based chat application (.NET backend challenge). Registered users chat in rooms via SignalR; a `/stock=stock_code` command triggers a **decoupled bot** that fetches a quote and posts it back through **RabbitMQ**. Judged on backend quality: standards, attention to detail, reusability. Due: **Friday, August 7**.

## Challenge requirements (source of truth)

Mandatory:
- Registered users log in and talk in a chatroom.
- `/stock=stock_code` command in chat (e.g., `/stock=aapl.us`).
- Decoupled bot calls `https://stooq.com/q/l/?s={stock_code}&f=sd2t2ohlcv&h&e=csv`, parses the CSV, and replies to the chatroom via RabbitMQ with: `"AAPL.US quote is $93.42 per share"`. Post owner = bot.
- Messages ordered by timestamp; show only the **last 50**.
- Unit tests for chosen functionality.

Hard constraints:
- The stock command is **never saved** to the database as a post.
- Frontend as simple as possible; backend is what's evaluated.
- No secrets committed. Local Git history required (deliverable includes `.git/`).
- Watch resource consumption (connections, queries, broadcasts).

Bonus (track status honestly — reported in delivery email):
- [ ] Multiple chatrooms
- [ ] .NET Identity authentication
- [ ] Bot handles unknown commands/exceptions gracefully
- [ ] Installer

## Architecture

Clean Architecture + DDD + CQRS-style command handlers. Two runnable processes: `Chat.Web` (host + SignalR) and `Chat.Bot` (worker). See `.claude/skills/clean-architecture/SKILL.md` for the full rules.

```
src/
  Chat.Domain/          # Result/Error kernel; Message + ChatRoom aggregates and their VOs; ChatCommandParser. Zero deps.
  Chat.Application/     # Abstractions/ (8 ports), Behaviors/, Contracts/ (MessageDto, wire records). Features/ from 1.8.
  Chat.Infrastructure/  # ChatDbContext (EF Core + Identity), converters/configurations/migrations/repositories, MassTransit wiring, health checks, clock.
  Chat.Web/             # Composition root, Razor Pages, health endpoints. Identity UI from 1.11, SignalR hub from 1.12.
  Chat.Bot/             # Request consumer + quote worker + /health. No persistence, ever.
tests/
  Chat.UnitTests/       # 209 tests: domain, port shape, EF model and the generated read SQL.
  Chat.IntegrationTests/  # empty until 1.17.
```

Dependency rule: Web/Bot → Infrastructure → Application → Domain. Never the other way. `AbstractionsTests` asserts the compiled `Chat.Application` assembly references no EF Core, MassTransit, ASP.NET Core or RabbitMQ.Client.

Stock flow: hub → `PostMessageCommand` → parser detects `/stock=` → `RequestStockQuoteCommand` → publish `StockQuoteRequested` → Bot consumes on `stock-quote-requests` → quote provider → publish `StockQuoteResolved` → Web consumes on `stock-quote-responses` → posts + broadcasts to the room as "Bot". The **command** is never written to the DB.

Messaging uses **MassTransit 8 over RabbitMQ**. MassTransit owns the exchange layout (one exchange per message type) and the `_error` / `_skipped` queues; we own the receive endpoint names `stock-quote-requests` / `stock-quote-responses`, prefetch and the retry policy, all in `Chat.Application/Contracts/Messaging/MessagingConstants.cs`. `AddMessaging(configuration, registerConsumers)` configures the bus; each host passes only its own consumers. The wire contracts (`StockQuoteRequested`, `StockQuoteResolved`) are plain records — no separate Contracts project. Persistence is EF Core + SQL Server 2022 (database `ChatDb`, run in Docker via `docker-compose.dev.yml`), sharing one `ChatDbContext` with Identity; `20260804190713_InitialCreate` is committed and applied. Bot quote answers **are** persisted (`MessageOrigin.Bot`) and then broadcast; only the `/stock=` command is never persisted. Full design: `docs/ARCHITECTURE.md`.

## Conventions

- Ubiquitous language: ChatRoom, Message, StockCommand, StockQuote, Participant.
- Result pattern for expected failures; exceptions only for exceptional cases (a null value object is a programmer error, not a `Result`).
- Value objects: private constructor + static `Create` returning `Result<T>` + a nested `public static class Errors` of `Error` constants with stable `"Type.Code"` codes (`MessageContent.TooLong`).
- Aggregates: factories only, plus a private parameterless constructor purely for EF materialisation and `private init` properties — mappings must keep EF's **default backing-field access mode**.
- Repositories only stage (`Add`, synchronous, no I/O); `IUnitOfWork.SaveChangesAsync` commits exactly once per use case, which is what makes "this path performs no write" provable in a handler test.
- Read paths return DTOs projected in SQL; entities never leave a repository on the query side. `MessageDto` lives in `Chat.Application/Contracts/Messages/` — three ports share it, so it is not in a feature folder.
- Nullable enabled; async all the way with CancellationToken last; `sealed` + `record` defaults; culture-sensitive APIs banned in domain logic (`OrdinalIgnoreCase`, `ToLowerInvariant`, `InvariantCulture`).
- Tests: xUnit + FluentAssertions; naming `Scenario_Condition_ExpectedOutcome`.
- Constants: `LatestMessagesCount = 50`; endpoint names in `MessagingConstants`; column widths in `PersistenceConstants`.
- Commit per completed task, imperative messages.
- Central package management: all versions in `Directory.Packages.props`, never in a csproj.
- Licence-pinned: MediatR stays on 12.x (Apache-2.0), FluentAssertions on 7.x, MassTransit on 8.x. Do not upgrade — the next major of each is commercially licensed.
- LF line endings everywhere (`.editorconfig` + `.gitattributes`); `dotnet format` enforces it.
- Primary constructors are preferred; captured parameters stay camelCase, explicit fields use `_`.
- Requests implement `ICommand`/`ICommand<T>`/`IQuery<T>` so the `Result`-constrained pipeline behaviors apply; FluentValidation runs in that pipeline and hubs/controllers stay ~5 lines.

## Commands

```bash
cp .env.example .env                          # once — local-only credentials
dotnet tool restore                           # once — pins dotnet-ef 10.0.10 (.config/dotnet-tools.json)
docker compose -f docker-compose.dev.yml up -d   # SQL Server 2022 + RabbitMQ 4 (UI: http://localhost:15672)
docker compose -f docker-compose.dev.yml ps      # wait for both to report (healthy)
dotnet build                                  # 0 warnings expected (TreatWarningsAsErrors)
dotnet test                                   # 539 unit (hermetic) + 19 integration (needs Docker)
dotnet format                                 # before committing
dotnet format --verify-no-changes             # CI-style gate

dotnet ef migrations add <Name> -p src/Chat.Infrastructure -s src/Chat.Web
dotnet ef database update -p src/Chat.Infrastructure -s src/Chat.Web
dotnet run --project src/Chat.Web             # terminal 1 — http://localhost:5271
dotnet run --project src/Chat.Bot             # terminal 2 — http://localhost:5299 (health only)
```

**Running both hosts together** (the stock flow needs both):

```bash
pwsh ./scripts/run-dev.ps1                    # builds, starts both, waits for /health/live, Ctrl+C stops both
pwsh ./scripts/run-dev.ps1 -SkipBuild         # skip the build step
```

In Visual Studio 2026 pick the **"Chat.Web + Chat.Bot"** startup profile (defined in `Chat.slnLaunch`)
instead of setting a single startup project.

**Health endpoints** — both hosts expose the same three routes:

```bash
curl http://localhost:5271/health             # Chat.Web: sql-server + rabbitmq + masstransit-bus
curl http://localhost:5299/health             # Chat.Bot: rabbitmq + stooq + masstransit-bus
curl http://localhost:5271/health/ready       # readiness-tagged dependencies only
curl http://localhost:5271/health/live        # process liveness, runs no dependency probe
```

Copy `.env.example` to `.env` before `docker compose up`. The DB connection string and broker credentials come from user-secrets or `ConnectionStrings__ChatDatabase` / `RabbitMq__*` environment variables — never `appsettings.json`. `AddPersistence` throws at startup when the connection string is missing.

`pwsh ./scripts/set-dev-secrets.ps1 [-FinnhubApiKey <key>]` writes them all from `.env` — prefer it, since hand-copying is how `.env` and the secret stores drift apart (the broker then rejects the login the apps send). Visual Studio reads the same store, so *Manage User Secrets* shows what the script wrote. The manual equivalent:

```bash
dotnet user-secrets set "ConnectionStrings:ChatDatabase" \
  "Server=127.0.0.1,1433;Database=ChatDb;User Id=sa;Password=<from .env>;Encrypt=True;TrustServerCertificate=True" \
  --project src/Chat.Web
dotnet user-secrets set "RabbitMq:UserName" "<from .env>" --project src/Chat.Web
dotnet user-secrets set "RabbitMq:Password" "<from .env>" --project src/Chat.Web
dotnet user-secrets set "RabbitMq:UserName" "<from .env>" --project src/Chat.Bot
dotnet user-secrets set "RabbitMq:Password" "<from .env>" --project src/Chat.Bot

# Quote provider — Chat.Bot only. Finnhub is the default; the key is what makes a real price possible.
dotnet user-secrets set "Finnhub:ApiKey" "<free key from finnhub.io>" --project src/Chat.Bot
dotnet user-secrets set "Stocks:Provider" "Stooq" --project src/Chat.Bot   # to select Stooq instead
```

## Workflow (Claude Code)

- `/architect` — design/evolve architecture, generate `docs/PLAN.md` task list.
- `/implement <task>` — implement a plan task with tests, quality gates, commit.
- `/test [target]` — write/fix tests until green. `/review [scope]` — standards + challenge-compliance review.
- `/commit [msg]` — format + build + test + secret scan + commit. `/update-readme`, `/update-claude-md` — keep docs synced (README is a graded deliverable).

Agents: `architect`, `implementer`, `test-engineer`, `code-reviewer`, `docs-maintainer`. Skills in `.claude/skills/` cover clean-architecture, ddd-patterns, cqrs-command-handlers, clean-code, security, performance, unit-testing, integration-testing, docs-maintenance.

## Status

- Authoritative task list: `docs/PLAN.md` — each done task carries its design record, and later tasks must conform to it.
- [x] Phase 0 (0.1–0.9): solution, build/package governance, compose stack, health checks, MassTransit.
- [x] Phase 1 domain + persistence (1.1–1.7): message/room value objects, `ChatCommandParser`, `Message` and `ChatRoom` aggregates, the eight Application ports, EF Core + Identity model and the applied `InitialCreate` migration.
- [x] Phase 1 mandatory (1.8–1.17): handlers, MassTransit publishers/endpoints, Identity + auth, hub, chat page with help panel, quote providers, bot worker, response consumer, integration suite. The full `/stock=` round trip works end to end.
- [ ] 1.18 recorded manual walkthrough; bonuses 2.2 (multiple rooms), 2.4 (rate limit), 2.5 (installer).
- [ ] Bonus features (see checklist above). `ApplicationUser` and the Identity tables exist from 1.7, but authentication and the UI are task 1.11 — the Identity bonus is not claimable yet.
- [ ] README written and verified (task 3.1 — `README.md` does not exist yet).
- [ ] Final review + delivery zip/repo (include `.git/`).

## Gotchas

- **Two quote providers sit behind `IStockQuoteProvider`, chosen by `Stocks:Provider`**: `Finnhub` (**default**, needs `Finnhub:ApiKey` for a real price) and `Stooq`. Without a key the bot answers a friendly failure and logs the gap — nothing else breaks. Stooq's CSV endpoint is unreachable from a server — `/q/l/` is 404 and `/q/d/l/` serves a JavaScript proof-of-work browser check (429 on an unsolved `/__verify`). **Do not implement a solver for it**; that is circumventing an access control, and Finnhub exists because it is an API built for programmatic access. Verified live: `AAPL.US quote is $311.51 per share`.
- Unknown symbol is a **friendly answer with no banner** (`SymbolNotFound`), and only a service failure raises the red banner (`LookupFailed`). Stooq signals it with `N/D` in a CSV row; Finnhub with HTTP 200 and every number zero. `Access denied` from Stooq is **not** an unknown symbol — measured returning identically for a valid ticker — so it is a refusal. Never build the Stooq URL from raw input: `StockCode.Create` already normalises to lower case and enforces `^[a-z0-9.\-]{1,20}$` (anchored `\A`/`\z`, because in .NET `$` also matches before a trailing newline).
- Reviewers will open 2 browsers with 2 users: broadcast via SignalR groups per room, identity from claims (never from client payload).
- `Messages` deliberately has **no foreign key at all**. The bot's author id `system:bot` is not an Identity user, so an FK to `AspNetUsers` would reject every quote answer the challenge requires; `ChatRoomId` is validated with `ExistsAsync` instead.
- EF Core cannot translate member access on a value-converted type (`message.Content.Value`), so the "last 50" query projects raw columns into `MessageRepository.LatestMessageRow` and one in-memory loop reverses and unwraps them. Do not "simplify" it back into a `MessageDto` projection.
- `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` are on: unused usings and style violations fail the build (`GenerateDocumentationFile=true` makes IDE0005 fire; `CS1591` is suppressed). The test projects already declare `Xunit` and `FluentAssertions` as global usings, so adding those `using` lines to a test file fails with IDE0005.
- Generated migrations are declared `generated_code = true` in a folder-scoped `.editorconfig` section — `dotnet ef` emits block-scoped namespaces and an unused `using System;`, which would otherwise fail the build. Nothing else is relaxed.
- `dotnet build` can report 0 warnings while `dotnet format --verify-no-changes` still fails on whitespace and layout (measured). Run `dotnet format` before every commit, not just the build.
- `MSSQL_SA_PASSWORD` is baked into the SQL Server volume on first start; changing it in `.env` later needs `docker compose -f docker-compose.dev.yml down -v`, and it must satisfy SQL Server's complexity policy or the container exits at boot. First start takes ~30 s; the compose healthcheck covers it and `AddPersistence` uses `EnableRetryOnFailure` so `dotnet run` does not race the container.
- Connection strings must carry `TrustServerCertificate=True` locally — the container uses a self-signed certificate and Microsoft.Data.SqlClient 4+ encrypts by default.
- `dotnet test` prints "No test is available" for `Chat.IntegrationTests` until task 1.17 lands; exit code is still 0.
- `Chat.Bot` must never call `AddPersistence()` — that is the structural guarantee it stays decoupled from the database. It is a `Microsoft.NET.Sdk.Web` host purely so it can serve `/health`; it exposes no chat surface.
- `InvariantGlobalization` must stay `false`. `Microsoft.Data.SqlClient` throws "Globalization Invariant Mode is not supported" at connection time, so EF Core and the SQL health check both fail under it.
- Use `127.0.0.1`, never `localhost`, in connection strings and broker host names. The compose file publishes on IPv4 loopback only, but `localhost` resolves to `::1` first on Windows — SqlClient then burns its full 15 s timeout before failing.
- Health checks live in `Chat.Infrastructure/HealthChecks` and are mapped by `MapChatHealthChecks()`. Stooq is tagged `external` and excluded from `/health/ready` — a third-party outage must not mark the bot unready.
- MassTransit's `masstransit-bus` check reports **bus lifecycle state, not broker reachability**: measured staying `Healthy` through a 60 s broker outage because a bus with no receive endpoints never connects. That is why `RabbitMqHealthCheck` still exists; re-measure in task 1.10 and delete ours if the bus check then detects the outage.
