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
  Chat.Infrastructure/  # EF Core + Identity, RabbitMQ publisher/consumer, Stooq typed HttpClient.
  Chat.Web/             # SignalR hub(s), minimal UI, composition root.
  Chat.Bot/             # BackgroundService: consume stock requests -> Stooq -> publish quote.
tests/
  Chat.UnitTests/
  Chat.IntegrationTests/
```

Dependency rule: Web/Bot → Infrastructure → Application → Domain. Never the other way.

Stock flow: hub → `PostMessageCommand` → parser detects `/stock=` → `RequestStockQuoteCommand` → publish to `stock.quote.requests` queue → Bot consumes → Stooq CSV → publish to `stock.quote.responses` → Web consumer broadcasts to the room as "Bot". No DB write anywhere in this path.

## Conventions

- Ubiquitous language: ChatRoom, Message, StockCommand, StockQuote, Participant.
- Result pattern for expected failures; exceptions only for exceptional cases.
- FluentValidation in pipeline behavior; hubs/controllers stay ~5 lines.
- Nullable enabled; async all the way with CancellationToken; `sealed` + `record` defaults.
- Tests: xUnit + FluentAssertions; naming `Scenario_Condition_ExpectedOutcome`.
- Constants: `LatestMessagesCount = 50`; queue names in `MessagingConstants`.
- Commit per completed task, imperative messages.

## Commands

```bash
docker compose up -d                          # RabbitMQ (+ DB if containerized)
dotnet ef database update -p src/Chat.Infrastructure -s src/Chat.Web
dotnet run --project src/Chat.Web             # terminal 1
dotnet run --project src/Chat.Bot             # terminal 2
dotnet test                                   # all tests
dotnet format                                 # before committing
```

(Verify/update these once the solution is scaffolded — see /update-claude-md.)

## Workflow (Claude Code)

- `/architect` — design/evolve architecture, generate `docs/PLAN.md` task list.
- `/implement <task>` — implement a plan task with tests, quality gates, commit.
- `/test [target]` — write/fix tests until green.
- `/review [scope]` — standards + challenge-compliance review before committing.
- `/commit [msg]` — format + build + test + secret scan + commit.
- `/update-readme`, `/update-claude-md` — keep docs synced (README is a graded deliverable).

Agents: `architect`, `implementer`, `test-engineer`, `code-reviewer`, `docs-maintainer`. Skills in `.claude/skills/` cover clean-architecture, ddd-patterns, cqrs-command-handlers, clean-code, security, performance, unit-testing, integration-testing, docs-maintenance.

## Status

- [ ] Architecture designed, PLAN.md created
- [ ] Solution scaffolded
- [ ] Mandatory features implemented
- [ ] Bonus features (see checklist above)
- [ ] README finalized and verified
- [ ] Final review + delivery zip/repo (include `.git/`)

## Gotchas

- Stooq returns `N/D` fields for unknown tickers — bot must answer with a friendly "not found" message, not crash.
- Stooq ticker format is lowercase with market suffix (`aapl.us`); normalize and validate (`^[a-z0-9.\-]{1,20}$`) before building the URL.
- RabbitMQ may not be ready when apps start locally — use resilient connection/retry on startup.
- Reviewers will open 2 browsers with 2 users: broadcast via SignalR groups per room, identity from claims (never from client payload).
