# CLAUDE.md — Chat + Stock Bot Challenge (.NET)

Browser-based chat application (.NET backend challenge). Registered users chat in rooms via SignalR; a `/stock=stock_code` command triggers a **decoupled bot** that fetches a quote from Stooq and posts it back through **RabbitMQ**. Judged on backend quality: standards, attention to detail, reusability. Due: **Friday, August 7**.

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
  Chat.Domain/          # Entities, VOs (StockCode, MessageContent), domain events. No external deps.
  Chat.Application/     # Features/<UseCase>/ Command|Query + Handler + Validator; interfaces for infra.
  Chat.Infrastructure/  # EF Core + Identity, MassTransit bus + consumers, Stooq typed HttpClient.
  Chat.Web/             # SignalR hub(s), minimal UI, health endpoints, composition root.
  Chat.Bot/             # BackgroundService: consume stock requests -> Stooq -> publish quote; health endpoints.
tests/
  Chat.UnitTests/
  Chat.IntegrationTests/
```

Dependency rule: Web/Bot → Infrastructure → Application → Domain. Never the other way.

Stock flow: hub → `PostMessageCommand` → parser detects `/stock=` → `RequestStockQuoteCommand` → publish `StockQuoteRequested` → Bot consumes on `stock-quote-requests` → Stooq CSV → publish `StockQuoteResolved` → Web consumes on `stock-quote-responses` → posts + broadcasts to the room as "Bot". The **command** is never written to the DB.

Messaging uses **MassTransit 8 over RabbitMQ**. MassTransit owns the exchange layout (one exchange per message type) and the `_error` / `_skipped` queues; we own the receive endpoint names `stock-quote-requests` / `stock-quote-responses`, prefetch and the retry policy, all in `Chat.Application/Contracts/Messaging/MessagingConstants.cs`. `AddMessaging(configuration, registerConsumers)` configures the bus; each host passes only its own consumers. The wire contracts (`StockQuoteRequested`, `StockQuoteResolved`) are plain records in `Chat.Application/Contracts/Messaging` — no separate Contracts project, and Application takes no MassTransit dependency. Persistence is EF Core + SQL Server 2022 (database `ChatDb`, run in Docker via `docker-compose.dev.yml`), sharing one `ChatDbContext` with Identity. Bot quote answers **are** persisted (`MessageOrigin.Bot`) and then broadcast; only the `/stock=` command is never persisted. Full design: `docs/ARCHITECTURE.md`.

## Conventions

- Ubiquitous language: ChatRoom, Message, StockCommand, StockQuote, Participant.
- Result pattern for expected failures; exceptions only for exceptional cases.
- FluentValidation in pipeline behavior; hubs/controllers stay ~5 lines.
- Nullable enabled; async all the way with CancellationToken; `sealed` + `record` defaults.
- Tests: xUnit + FluentAssertions; naming `Scenario_Condition_ExpectedOutcome`.
- Constants: `LatestMessagesCount = 50`; queue names in `MessagingConstants`.
- Commit per completed task, imperative messages.
- Central package management: all versions in `Directory.Packages.props`, never in a csproj.
- Licence-pinned: MediatR stays on 12.x (Apache-2.0), FluentAssertions on 7.x, MassTransit on 8.x. Do not upgrade — the next major of each is commercially licensed.
- LF line endings everywhere (`.editorconfig` + `.gitattributes`); `dotnet format` enforces it.
- Primary constructors are preferred; captured parameters stay camelCase, explicit fields use `_`.
- Requests implement `ICommand`/`ICommand<T>`/`IQuery<T>` so the `Result`-constrained pipeline behaviors apply.
- Read paths return DTOs projected in SQL; entities never leave a repository on the query side.

## Commands

```bash
cp .env.example .env                          # once — local-only credentials
dotnet tool restore                           # once — pins dotnet-ef 10.0.10 (.config/dotnet-tools.json)
docker compose -f docker-compose.dev.yml up -d   # SQL Server 2022 + RabbitMQ 4 (UI: http://localhost:15672)
docker compose -f docker-compose.dev.yml ps      # wait for both to report (healthy)
dotnet build                                  # 0 warnings expected (TreatWarningsAsErrors)
dotnet test                                   # all tests
dotnet format                                 # before committing
dotnet format --verify-no-changes             # CI-style gate

# EF commands only work from task 1.7 onward, once ChatDbContext exists.
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

Copy `.env.example` to `.env` before `docker compose up`. The DB connection string and broker credentials come from user-secrets or `ConnectionStrings__ChatDatabase` / `RabbitMq__*` environment variables — never `appsettings.json`.

```bash
dotnet user-secrets set "ConnectionStrings:ChatDatabase" \
  "Server=127.0.0.1,1433;Database=ChatDb;User Id=sa;Password=<from .env>;Encrypt=True;TrustServerCertificate=True" \
  --project src/Chat.Web
dotnet user-secrets set "RabbitMq:UserName" "<from .env>" --project src/Chat.Web
dotnet user-secrets set "RabbitMq:Password" "<from .env>" --project src/Chat.Web
dotnet user-secrets set "RabbitMq:UserName" "<from .env>" --project src/Chat.Bot
dotnet user-secrets set "RabbitMq:Password" "<from .env>" --project src/Chat.Bot
```

## Workflow (Claude Code)

- `/architect` — design/evolve architecture, generate `docs/PLAN.md` task list.
- `/implement <task>` — implement a plan task with tests, quality gates, commit.
- `/test [target]` — write/fix tests until green.
- `/review [scope]` — standards + challenge-compliance review before committing.
- `/commit [msg]` — format + build + test + secret scan + commit.
- `/update-readme`, `/update-claude-md` — keep docs synced (README is a graded deliverable).

Agents: `architect`, `implementer`, `test-engineer`, `code-reviewer`, `docs-maintainer`. Skills in `.claude/skills/` cover clean-architecture, ddd-patterns, cqrs-command-handlers, clean-code, security, performance, unit-testing, integration-testing, docs-maintenance.

## Status

- [x] Architecture designed, PLAN.md created (`docs/ARCHITECTURE.md`, `docs/PLAN.md`)
- [x] Solution scaffolded (7 projects, build + tests + format green)
- [ ] Mandatory features implemented
- [ ] Bonus features (see checklist above)
- [ ] README finalized and verified
- [ ] Final review + delivery zip/repo (include `.git/`)

## Gotchas

- Stooq returns `N/D` fields for unknown tickers — bot must answer with a friendly "not found" message, not crash.
- Stooq ticker format is lowercase with market suffix (`aapl.us`); normalize and validate (`^[a-z0-9.\-]{1,20}$`) before building the URL.
- RabbitMQ may not be ready when apps start locally — use resilient connection/retry on startup.
- Reviewers will open 2 browsers with 2 users: broadcast via SignalR groups per room, identity from claims (never from client payload).
- `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` are on: unused usings and style violations fail the build. `GenerateDocumentationFile=true` is required for IDE0005 to fire; `CS1591` is suppressed.
- `MSSQL_SA_PASSWORD` is baked into the SQL Server volume on first start; changing it in `.env` later needs `docker compose -f docker-compose.dev.yml down -v`. It must satisfy SQL Server's complexity policy (8+ chars, upper/lower/digit/symbol) or the container exits at boot.
- SQL Server needs ~30 s on first start. The compose healthcheck covers it, and `AddPersistence` uses `EnableRetryOnFailure` so `dotnet run` does not race the container.
- Connection strings must carry `TrustServerCertificate=True` locally — the container uses a self-signed certificate and Microsoft.Data.SqlClient 4+ encrypts by default.
- `dotnet test` prints "No test is available" for `Chat.IntegrationTests` until task 1.17 lands; exit code is still 0.
- `Chat.Bot` must never call `AddPersistence()` — that is the structural guarantee it stays decoupled from the database. It is a `Microsoft.NET.Sdk.Web` host purely so it can serve `/health`; it exposes no chat surface.
- `InvariantGlobalization` must stay `false`. `Microsoft.Data.SqlClient` throws "Globalization Invariant Mode is not supported" at connection time, so EF Core and the SQL health check both fail under it. Use `CultureInfo.InvariantCulture` explicitly in parsing/formatting code instead.
- Use `127.0.0.1`, never `localhost`, in connection strings and broker host names. The compose file publishes on IPv4 loopback only, but `localhost` resolves to `::1` first on Windows — SqlClient then burns its full 15 s timeout before failing.
- Health checks are registered in `Chat.Infrastructure/HealthChecks` and mapped by `MapChatHealthChecks()`. Stooq is tagged `external` and deliberately excluded from `/health/ready` — a third-party outage must not mark the bot unready.
- MassTransit's `masstransit-bus` health check reports **bus lifecycle state, not broker reachability**: measured staying `Healthy` through a 60 s broker outage because a bus with no receive endpoints never opens a connection. That is why `RabbitMqHealthCheck` still exists. Re-measure in task 1.10 once receive endpoints are registered, and delete ours if the bus check then detects the outage.
- Do not hand-declare exchanges, routing keys or dead-letter queues — MassTransit owns exchange layout and creates `<queue>_error` / `<queue>_skipped` itself.
