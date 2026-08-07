# Financial Chat — .NET Chat + Stock Bot

A browser chat application where registered users log in and talk in a chatroom over SignalR. Typing
`/stock=aapl.us` is not a chat message: it is a command that is published to RabbitMQ, picked up by a
**separate process** (`Chat.Bot`) that has no database access at all, resolved against a quote service, and
published back so the web host posts the answer into the room as the **bot**. The command itself is never
written to the database — only the bot's answer is.

Two quote providers ship behind one port: **Finnhub** (a keyed JSON API, the default) and **Stooq** (the
endpoint the challenge names). Stooq's CSV endpoint became unreachable from a server while this was built,
which is why the second one exists and is now the default — see
[Quote providers](#quote-providers--stooq-and-finnhub).

**Running live:** [chat-stock-app.joya.services](https://chat-stock-app.joya.services) — register two users
in two browser windows and try `/stock=aapl.us`. Both processes report their own dependencies:
[the chat's `/health`](https://chat-stock-app.joya.services/health) (SQL Server, RabbitMQ, the bus) and
[the bot's `/health`](https://chat-stock-bot.joya.services/health), which also names the quote provider it is
configured for. Everything below also runs locally; see [Deployment](#deployment-docker--coolify) for how
that instance is put together.

## Mandatory requirements and where they are

| Requirement | Where |
| --- | --- |
| Registered users log in and chat in a room | ASP.NET Core Identity (`src/Chat.Web/Areas/Identity`), `ChatHub` (`src/Chat.Web/Hubs/ChatHub.cs`) |
| `/stock=stock_code` command | `ChatCommandParser` + `StockCode` (`src/Chat.Domain/StockCommands/`) |
| Decoupled bot over RabbitMQ | `src/Chat.Bot` (no `AddPersistence()`, no SignalR, no reference to `Chat.Web`) |
| Quote call and `"AAPL.US quote is $93.42 per share"` | `StooqClient` or `FinnhubClient` behind `IStockQuoteProvider` (`src/Chat.Infrastructure/Stocks/`), `StockQuoteAnswer` |
| Post owner is the bot | `Message.PostByBot` — takes no author, uses `MessageAuthor.Bot` |
| Ordered by timestamp, last 50 only | `MessageRepository.GetLatestAsync`, capped in `GetLatestMessagesValidator` |
| The stock command is never persisted | Enforced structurally in four layers — see [Design decisions](#design-decisions) |
| Unit tests | 632 tests in `tests/Chat.UnitTests`, plus 29 in `tests/Chat.IntegrationTests` |

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

Each host has its own secret store, keyed by the `UserSecretsId` in its `.csproj`
(`chat-stock-bot-web` and `chat-stock-bot-worker`). What each one needs, and why:

| Setting | `Chat.Web` | `Chat.Bot` | What it is |
| --- | --- | --- | --- |
| `ConnectionStrings:ChatDatabase` | **required** | — | SQL Server. The bot has no database *by design*, so it has no connection string |
| `RabbitMq:UserName` / `RabbitMq:Password` | **required** | **required** | Both processes talk to the broker; that is the only thing they share |
| `Stocks:Provider` | — | optional | `Finnhub` (default) or `Stooq` |
| `Finnhub:ApiKey` | — | **required** for a real price | Free key from finnhub.io. Only the bot calls a quote service; without it the bot answers a friendly failure |

**Mistyped settings stop the host, not a chat message.** The quote-provider sections are validated with
`ValidateDataAnnotations().ValidateOnStart()`, plus two checks data annotations cannot express: the base
address must be *absolute* and the quote path must contain the `{0}` the stock code is written into. Both
matter — measured, `Finnhub:BaseAddress=not a uri` binds happily to a valid **relative** `Uri`, satisfying
`[Required]`, and would then throw inside the bot's consumer, costing four delivery attempts and a
dead-lettered request before anyone saw the typo. A missing `Finnhub:ApiKey` is deliberately *not* a
validation failure: running keyless is a supported degraded mode, reported by `/health` and answered
politely in chat.

**The short way — one command for all of it.** Everything except the Finnhub key already lives in `.env`,
so a script reads it and writes each value to the host that needs it:

```bash
pwsh ./scripts/set-dev-secrets.ps1                            # from .env
pwsh ./scripts/set-dev-secrets.ps1 -FinnhubApiKey "<key>"      # ...and the key
```

Re-running is safe, and it never prints a value. Prefer this over copying by hand: `.env` is what
`docker compose` used to *create* the containers, so a typo there means the broker rejects the login the
apps send. Keeping one source removes that class of failure. The manual equivalent follows, in case you
would rather see each key.

Visual Studio reads the very same store — *right-click the project → Manage User Secrets* opens the file
this script writes, so configure it once and both the CLI and the IDE are set.

**SQL Server — `Chat.Web` only:**

```bash
dotnet user-secrets set "ConnectionStrings:ChatDatabase" "Server=127.0.0.1,1433;Database=ChatDb;User Id=sa;Password=<MSSQL_SA_PASSWORD from .env>;Encrypt=True;TrustServerCertificate=True" --project src/Chat.Web
```

`TrustServerCertificate=True` is required locally: the container uses a self-signed certificate and
Microsoft.Data.SqlClient encrypts by default. Use `127.0.0.1`, never `localhost` — see the note above.

**RabbitMQ — both hosts**, same credentials as `.env`:

```bash
dotnet user-secrets set "RabbitMq:UserName" "<RABBITMQ_USER from .env>"     --project src/Chat.Web
dotnet user-secrets set "RabbitMq:Password" "<RABBITMQ_PASSWORD from .env>" --project src/Chat.Web
dotnet user-secrets set "RabbitMq:UserName" "<RABBITMQ_USER from .env>"     --project src/Chat.Bot
dotnet user-secrets set "RabbitMq:Password" "<RABBITMQ_PASSWORD from .env>" --project src/Chat.Bot
```

**Quote provider — `Chat.Bot` only.** Finnhub is the default and needs a free key from
[finnhub.io](https://finnhub.io) to return a real price:

```bash
dotnet user-secrets set "Finnhub:ApiKey" "<your-api-key>" --project src/Chat.Bot
```

Skip it and everything still runs — the bot simply answers `I could not reach the quote service…` and
shows the red banner. To select Stooq instead, add
`dotnet user-secrets set "Stocks:Provider" "Stooq" --project src/Chat.Bot`; see
[Quote providers](#quote-providers--stooq-and-finnhub) for why that is no longer the default.

**Checking and clearing:**

```bash
dotnet user-secrets list  --project src/Chat.Bot     # prints keys and values — mind your screen
dotnet user-secrets clear --project src/Chat.Bot     # start over
```

**Environment variables instead of user-secrets** — for containers or CI, where `__` replaces the `:`:

```bash
ConnectionStrings__ChatDatabase=...   RabbitMq__UserName=...   RabbitMq__Password=...
Stocks__Provider=Finnhub              Finnhub__ApiKey=...
```

Environment variables are the last configuration source added, so they override `appsettings.json` *and*
user-secrets. Nothing here belongs in a committed file: `appsettings.json` ships empty placeholders, and
`AddPersistence` throws at startup if the connection string is missing rather than failing later on the
first message.

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
A dismissible **help panel** at the top of the chat page explains all of this in the UI itself.

4. In one window, type `/stock=aapl.us`.
   - The command is **never stored and never broadcast**. The window that typed it shows a grey
     `you typed — /stock=aapl.us` line so the sender can see something happened; that line is
     client-side only and disappears on reload, which is what keeps the command out of the database.
   - An unknown command (`/help`) or a rejected ticker (`/stock=a&b`) shows a red `not sent — …` line to
     the sender alone. Neither reaches the room.
   - A message from **`Bot`** appears in **both** windows.
   - **On the default Finnhub provider with `Finnhub:ApiKey` set**, that message reads
     `AAPL.US quote is $311.51 per share` — a real price. Verified end to end on 2026-08-06. This is the
     line the challenge specifies, so set the key before you walk through this step.
   - **Without a key**, or with `Stocks:Provider=Stooq` (whose CSV endpoint is no longer reachable from a
     server), the bot instead answers
     `I could not reach the quote service, so I have no price for AAPL.US right now.` and the window that
     asked also gets a red banner. Both paths are correct behaviour; see
     [Quote providers](#quote-providers--stooq-and-finnhub). `GET http://localhost:5299/health` names the
     configured provider and reports a missing key, so it tells you which of the two you are looking at.
   - Try `/stock=zzzznotreal.us` too: an unknown symbol gets a friendly
     `Sorry, I could not find a quote for ZZZZNOTREAL.US.` and **no** banner, because the service is
     working — only a service failure raises the banner.
5. Type an unknown command such as `/help`. Only the window that typed it sees an error
   (`Unknown command. The only command available is /stock=<code>, for example /stock=aapl.us.`); the
   room sees nothing and nothing is stored.
6. Refresh either window. The chat history — including the bot's answer, but not the command — reloads
   in timestamp order.
7. **Multiple rooms** (bonus). In one window, type a name into **New room** and press **Create and join**.
   That window switches to the empty room; the other window's **Room** list gains it immediately, without a
   reload. Now post in the new room: only the window that joined it sees the line. Switch the second window
   to the same room and both see each other again, with the new room's own history. Switch back to
   `General` and the earlier conversation is still there, unchanged.

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
curl http://localhost:5299/health    # masstransit-bus, rabbitmq, stock-quote-provider
```

`stock-quote-provider` **probes whichever provider `Stocks:Provider` selects**, and its `description` names
the one that answered — so `/health` tells you how the bot is configured without reading its settings:

```json
{ "name": "stock-quote-provider", "status": "Healthy",
  "description": "Finnhub responded 200.", "tags": ["external"] }
```

The check is named for the role rather than the vendor because the vendor is configuration. The same call
that registers the quote client resolves the provider here, so the probe cannot end up watching a service
the bot never calls. Verified live both ways: `Finnhub responded 200.` by default, and
`Stooq responded 200.` with `Stocks__Provider=Stooq`.

It reports one more thing nothing else does: with Finnhub selected and **no API key**, the check is
`Degraded` and says which key is missing and how to set it. That is the gap most likely to be left on a
fresh clone, and without this it surfaces only as the bot answering "I could not reach the quote service" —
which reads like a broken bot rather than missing setup.

The check is tagged `external` and is deliberately **excluded from `/health/ready`**: a third-party outage
must not mark the bot unready and get it restarted or pulled out of rotation, because the bot's own job —
consuming requests and answering politely — still works.

---

## Tests

```bash
dotnet test
```

**661 tests, all passing** — 632 unit tests and 29 integration tests.

`tests/Chat.UnitTests` (632) is **hermetic**: no containers, no broker, no network access, about four
seconds. Database behaviour is covered by translating EF Core queries offline (`ToQueryString()`) and by
SQLite in process memory where a real unique index is needed; messaging is covered by MassTransit's
in-memory `ITestHarness`; **both** quote providers are covered by a stubbed `HttpMessageHandler` pointed at
a deliberately-unroutable host so a bypassed stub fails loudly instead of calling the real service — and,
for Finnhub, without spending the free key's rate budget.

`tests/Chat.IntegrationTests` (29) splits by what it needs. Twenty host the real `Chat.Web` with
`WebApplicationFactory` against a **throwaway SQL Server container** (`Testcontainers.MsSql`, the same image
`docker-compose.dev.yml` uses), so those **need Docker**. The other nine — `StockQuoteResolutionTests`,
which drive the bot's half of the round trip — need nothing: the bot has no database by design, so they are
plain `[Fact]`s that run anywhere. The whole file takes 8–16 seconds. RabbitMQ is *not* needed: the bus is
replaced by MassTransit's in-memory test harness, which keeps the real publisher adapters, the real
response consumer and the endpoint names from `MessagingConstants`. It covers the anonymous hub connection
being rejected, register → login → chat page, posting and reading history back in order and capped at 50,
`/stock=aapl.us` publishing a broker request while creating **no** message row, a bot answer arriving over
the broker and being posted as the bot, two SignalR clients in one room seeing each other's lines, and — for
the multiple-rooms bonus — a post reaching only the room it was sent to, a switched connection going silent
on the room it left, and the chat page rendering a picker with every room in it.

**Without Docker the twenty container tests skip with a reason instead of failing**, so `dotnet test` still
exits 0 on a machine that has no daemon. `DockerEnvironment` asks Testcontainers itself whether an endpoint
answers, and `[DockerFact]` turns that into an xUnit skip reason naming what to start — an environmental
gap must not read as a broken build. The same switch is available deliberately:

```bash
CHAT_TESTS_SKIP_DOCKER=1 dotnet test    # PowerShell: $env:CHAT_TESTS_SKIP_DOCKER='1'
```

Measured with it set: 632 unit passed, then 9 passed / 20 skipped / 0 failed, exit code 0.

Other gates:

```bash
dotnet build                       # 0 warnings (TreatWarningsAsErrors + EnforceCodeStyleInBuild)
dotnet format --verify-no-changes  # formatting gate
```

### Continuous integration

`.github/workflows/pr-workflow.yml` runs the same gates on every pull request and on every commit that
lands on `main`. Two jobs, so unit feedback does not wait on containers:

| Job | Steps |
| --- | --- |
| **Build & Unit Tests** | restore → `dotnet format --verify-no-changes` → `dotnet build -c Release` → 632 hermetic tests |
| **Integration Tests** | pulls the SQL Server image, then runs the 29 tests against a throwaway container via Testcontainers |

The SDK comes from `global.json`, so CI and a developer's machine cannot drift, and NuGet is cached on
`Directory.Packages.props` — under central package management that file *is* the version graph. RabbitMQ is
never needed: the bus is MassTransit's in-memory harness.

Verified green on the runner: **632 / 632** unit and **29 / 29** integration, with nothing skipped. That
last part is worth checking on any CI that runs this suite — `[DockerFact]` turns an unavailable Docker
daemon into a *skip* and leaves the exit code 0, so a green tick alone does not prove the container tests
ran. Read the counts.

Formatting is a separate step from the build on purpose: `dotnet build` reports 0 warnings while whitespace
and layout violations are still present, measured on this repository more than once.

---

## Deployment (Docker / Coolify)

**Live:** the stack is deployed on a VPS behind Coolify and Cloudflare.

| What | URL |
| --- | --- |
| Chat | https://chat-stock-app.joya.services |
| Chat health | https://chat-stock-app.joya.services/health — `sql-server`, `rabbitmq`, `masstransit-bus` |
| Chat readiness / liveness | `…/health/ready` and `…/health/live` |
| Bot health | https://chat-stock-bot.joya.services/health — `rabbitmq`, `stock-quote-provider`, `masstransit-bus` |

Verified answering at the time of writing, and the bot's `/health` reports `stock-quote-provider: Healthy —
"Finnhub responded 200."` — so the deployment resolves real prices rather than the keyless fallback.

**During a redeploy, `/health` on the chat legitimately answers 503 while `/health/live` stays 200.** That is
the whole point of having both: the container is alive and its dependencies are not back yet. It is also why
the compose healthcheck probes liveness — a readiness probe there would let a database restart look like a
sick process and get it restarted, underneath the retry logic that was about to recover it.

`docker-compose.yml` is the deployable stack: both application processes plus SQL Server and RabbitMQ.
`docker-compose.dev.yml` remains the *development* file and starts infrastructure only.

```bash
docker compose up -d --build     # exactly what Coolify runs on a deploy
docker compose ps                # all four services should reach (healthy)
```

| Service | Host port | Container port | Notes |
| --- | --- | --- | --- |
| `chat-web` | **3100** | **3100** | the chat; `CHAT_WEB_PORT` overrides the host side |
| `chat-bot` | **3101** | **3101** | health only — `GET :3101/health` names the configured quote provider |
| `sqlserver` | 3102, **loopback only** | 1433 | off 1433 on the host so it cannot clash with another SQL Server there |
| `rabbitmq` | 3103, **loopback only** | 15672 | management UI. AMQP (5672) is not published at all |

The applications listen on **3100 and 3101 inside the container**, not on the .NET default of 8080, and
that is deliberate — see "Reverse proxy" below.

### How the Coolify application is configured

Coolify builds the images itself from this repository — nothing is pushed to a registry.

| Setting | Value |
| --- | --- |
| Build Pack | **Docker Compose** |
| Base Directory | `/` |
| Docker Compose Location | `/docker-compose.yml` |
| Domains | only `chat-web` (and optionally `chat-bot`) — see the table below |

A deploy is a `docker compose up --build` on the VPS: images are rebuilt, `chat-web` applies pending
migrations at startup and seeds the `General` room, then both hosts connect to the broker. Nothing has to be
run by hand — no `dotnet ef database update`, no queue declaration. The order is enforced by
`depends_on: condition: service_healthy`, so neither application starts before SQL Server accepts logins and
RabbitMQ reports running.

The database and the broker bind to `127.0.0.1`, so neither is on the public internet — reach them
through an SSH tunnel. Nothing in the application uses those mappings: `chat-web` and `chat-bot` resolve
`sqlserver` and `rabbitmq` over the compose network.

**Environment variables** (Coolify → Settings → Environment Variables):

| Variable | Required | Purpose |
| --- | --- | --- |
| `MSSQL_SA_PASSWORD` | **yes** | Baked into the volume on first start; changing it later needs the volume removed |
| `RABBITMQ_USER` / `RABBITMQ_PASSWORD` | **yes** | The broker login both hosts share |
| `FINNHUB_API_KEY` | no | Without it the bot answers a friendly failure and `/health` reports the gap |
| `STOCKS_PROVIDER` | no | `Finnhub` (default) or `Stooq` |
| `REQUIRE_SECURE_COOKIE` | no | Defaults to `true` — see below |

The three required ones are declared `${VAR:?...}`, so a missing value fails the deploy naming the
variable instead of starting a container that cannot connect.

**Serve it over HTTPS, or turn one switch off deliberately.** The Identity cookie is `Secure` outside
Development, and a browser will not return a `Secure` cookie over plain `http`. The symptom is not an
error — registration appears to work and then every page bounces back to the login screen. Reached
through a Coolify domain with TLS it needs no change: `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is
already set, so the app sees the proxy's `X-Forwarded-Proto`. If you deliberately browse
`http://<host>:3100`, set `REQUIRE_SECURE_COOKIE=false` and accept that the session cookie then travels
in the clear. Both paths were verified against the running stack.

### Reverse proxy (Coolify) — the one field that causes a 502

In Coolify's **Domains** field, the port after the colon is the **container** port the proxy forwards to,
not a public port. So `https://chat.example.com:3100` only works if the application really is listening on
3100 inside its container. Getting this wrong produces the most confusing failure in the whole setup:
every container reports `healthy` and the deployment succeeds, because the apps *are* fine — the proxy is
simply forwarding to a closed port, and the browser gets **502 Bad Gateway**.

That is why both applications listen on 3100/3101 rather than 8080: host port, container port and the port
written in the domain are one number per service, so there is nothing to mismatch.

| Service | Domain in Coolify |
| --- | --- |
| `chat-web` | `https://<your-domain>:3100` — the only one that needs a domain |
| `chat-bot` | `https://<your-domain>:3101`, and only if you want its `/health` public |
| `sqlserver` | **none.** Aligning its port would not help: the proxy routes HTTP and SQL Server speaks TDS, a binary protocol. An HTTPS router in front of it cannot work on any port |
| `rabbitmq` | **none.** The management UI is HTTP, so a router *could* reach it at `:15672` — but that publishes a broker console with administrator credentials |

**Behind Cloudflare, use SSL mode Full (strict).** In *Flexible* mode Cloudflare talks plain HTTP to the
origin, so ASP.NET Core sees scheme `http` and withholds the `Secure` Identity cookie — the silent login
loop described above, reached by a different route. Leave **WebSockets** enabled as well, or SignalR falls
back to long polling.

**Two more things worth knowing:**

- **Container healthchecks probe `/health/live`, not `/health/ready`.** Liveness runs no dependency
  probe, so a database or broker blip cannot get a healthy process restarted underneath its own retry
  logic — which is the distinction those two endpoints exist to make.
- **On Windows, ports 3100–3103 may be unusable locally.** Hyper-V reserves dynamic ranges (measured:
  3058–3357 on the development machine), and Docker then fails with "an attempt was made to access a
  socket in a way forbidden by its access permissions". It is a Windows quirk, not a compose problem —
  the Linux host is unaffected. Override `CHAT_WEB_PORT` and friends to test locally.

Images build from `src/Chat.Web/Dockerfile` and `src/Chat.Bot/Dockerfile`, both with the repository root
as context because central package management lives there. They are Debian-based rather than Alpine on
purpose: the Alpine images ship without ICU, and `Microsoft.Data.SqlClient` throws "Globalization
Invariant Mode is not supported" the moment it opens a connection. `chat-web` also mounts a volume for
its data-protection keys, so a redeploy does not sign every user out.

### Triggering a deployment

`.github/workflows/deploy-to-dev.yml` runs on a **pre-release** or on **workflow_dispatch**, and it will not
deploy anything it has not first verified:

1. restore, build, unit tests, integration tests — the full gate set;
2. `docker build` of **both** images. This costs a few minutes and is the only step that catches a
   Dockerfile which no longer restores, a failure the .NET build cannot see because it never enters a
   container;
3. only then a `curl` to the Coolify webhook, with `--fail-with-body` so a rejected call fails the job
   instead of leaving a green tick over an HTTP 401.

It needs two repository secrets in the `development` environment: `COOLIFY_WEBHOOK_DEVELOPMENT` and
`COOLIFY_TOKEN_DEVELOPMENT`. Coolify pulls the repository and runs the compose file itself, so the workflow
never ships an artefact — it verifies, then asks Coolify to start.

Coolify's own **Deployments** tab can also redeploy on demand, which is the quickest way to pick up a commit
without cutting a release.

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
  Chat.UnitTests/       503 tests, hermetic
  Chat.IntegrationTests/  11 tests over the real host; needs Docker, skips cleanly without it
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

**The bot's own decoupling is asserted, not just documented.** Its registrations live in
`Chat.Bot/BotServiceCollectionExtensions.AddBotServices` rather than inline in `Program.cs`, precisely so a
test can call them: `BotCompositionTests` fails if any registered service or implementation type comes from
EF Core, `Microsoft.Data.SqlClient`, ASP.NET Core Identity or either `Persistence` namespace. A top-level
statement file cannot be invoked from a test, so as long as the registrations lived there, "the bot never
calls `AddPersistence()`" was a comment that nothing enforced.

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

## Quote providers — Stooq and Finnhub

The bot reads prices through one port, `IStockQuoteProvider`, with **two interchangeable
implementations**. Which one runs is a single configuration value; nothing else in the application
changes, and neither the domain, the messaging, the hub nor the UI knows the difference.

| `Stocks:Provider` | Implementation | Endpoint | Needs a key |
| --- | --- | --- | --- |
| `Finnhub` *(default)* | `FinnhubClient` + `FinnhubQuoteParser` | `https://finnhub.io/api/v1/quote?symbol={code}` — JSON, key in a header | **yes** |
| `Stooq` | `StooqClient` + `StooqCsvParser` | `https://stooq.com/q/d/l/?s={code}&i=d` — CSV | no |

**Finnhub is the default because it is the one that returns a price.** It needs one free key from
[finnhub.io](https://finnhub.io):

```bash
dotnet user-secrets set "Finnhub:ApiKey" "<your-api-key>" --project src/Chat.Bot
```

Only `Chat.Bot` needs it — it is the only process that calls a quote service.

**The key is sent as the `X-Finnhub-Token` header, never in the query string.** Finnhub accepts both. A
credential in a URL is a credential in every application log, proxy access log and error report that URL
passes through; `IHttpClientFactory` does redact query values in its own request log, but that protection
depends on the `System.Net.Http.DisableUriRedaction` switch staying off and on nobody turning on extended
HTTP logging. A header needs no such assumption. Two tests pin it — one unit, one integration — asserting
the header carries the key and the URL does not contain it.

**Without a key** nothing breaks: the bot logs
`Finnhub:ApiKey is not configured, so no quote can be requested`, answers the room with
`I could not reach the quote service, so I have no price for AAPL.US right now.` and shows the asking
participant the red banner. Chat, history, the two-browser scenario and the "command is never persisted"
guarantee all work exactly as before — only the price is missing.

**To use Stooq instead** (no key, but see below — its CSV is no longer readable from a server):

```bash
dotnet user-secrets set "Stocks:Provider" "Stooq" --project src/Chat.Bot
```

A misspelled provider name fails at startup rather than silently falling back, so a typo cannot look like
the alternative quietly not being used.

### Why Finnhub is the default

The challenge names Stooq's CSV endpoint, and it is still implemented and selectable. It stopped being
usable from a server while this was being built, in two stages — both measured, both recorded below in
[A note about Stooq](#a-note-about-stooq):

1. The documented single-quote path `/q/l/?s=…&f=sd2t2ohlcv&h&e=csv` now answers **404**.
2. The surviving daily-history path `/q/d/l/?s=…&i=d` answers **200 with a JavaScript
   proof-of-work "verify your browser" page** to any client that is not a browser session which has
   already solved it. A browser passes it invisibly, which is why the file downloads by hand; an
   `HttpClient` receives the challenge instead.

Solving that proof-of-work in the bot was considered and **deliberately not done**. It exists to keep
automated clients out — `POST /__verify` with an unsolved nonce answers `429`, so it is enforced
server-side — and defeating it would be both a circumvention of the site's access control and a brittle
thing to hand a reviewer, breaking the moment the difficulty or format changes.

Finnhub is the honest answer to the same problem: an API **built for programmatic access**, which answers
an `HttpClient` by design and authenticates with a key instead of a browser check. Adding it cost one
adapter, because `IStockQuoteProvider` was designed as a port from the start — the seam was already there.

Verified live on 2026-08-06 with `Stocks:Provider=Finnhub`, driven through the real chat:

```
/stock=aapl.us          -> Bot: AAPL.US quote is $311.51 per share
/stock=msft.us          -> Bot: MSFT.US quote is $496.18 per share
/stock=zzzznotreal.us   -> Bot: Sorry, I could not find a quote for ZZZZNOTREAL.US.
```

The last line matters: an unknown symbol is a friendly answer with **no** red banner, because the service
is working. Finnhub signals it with HTTP 200 and every number zero — its equivalent of Stooq's `N/D`.

Two details the adapter handles:

- **Symbol translation.** The chat uses Stooq-style tickers; Finnhub names US listings without a suffix.
  `aapl.us` becomes `AAPL`, while other markets keep theirs (`shop.to` stays `SHOP.TO`).
- **A rejected key is logged as an error, not a warning.** A `401`/`403` is a configuration mistake an
  operator must fix, unlike a transient failure that will pass on its own.

---

## A note about Stooq

Measured from this machine on **2026-08-05**:

| Request | Result |
| --- | --- |
| `GET https://stooq.com/q/l/?s=aapl.us&f=sd2t2ohlcv&h&e=csv` | **404**, `text/html`, 271-byte "page does not exist" |
| the same URL with a desktop-browser `User-Agent` | **404**, `text/html` |
| the same path on `https://stooq.pl/` | **404**, `text/html` |
| `GET https://stooq.com/` | **200** |
| `GET https://stooq.com/q/d/l/?s=aa.us&i=d` (daily history) | **200**, but `text/html` — a browser-verification page, not CSV |
| the same, with a browser `User-Agent`, cookie jar and redirects followed | **200**, `text/html` — the same page |
| the same, from a browser session that had already passed the check | **200**, CSV — `Date,Open,High,Low,Close,Volume` plus one line per session |
| an unknown ticker, from that same verified session | **200**, body `Access denied` — *not* a 404 |

Two separate things are going on.

**The single-quote path the challenge documents (`/q/l/`) has been withdrawn** — it answers 404 while the
site itself answers 200.

**The daily-history download (`/q/d/l/`) still exists, but is behind a browser check.** It answers 200 with
a page containing `This site requires JavaScript to verify your browser` plus a script that computes a
SHA-256 proof-of-work, POSTs the nonce to `/__verify`, and only then reloads to receive the CSV. A browser
does that automatically, which is why the download works interactively. An `HttpClient` receives the
challenge page instead. Solving that proof-of-work in the bot is not implemented: it exists specifically to
keep automated clients out, so defeating it is not the right answer to a broken endpoint.

What *is* implemented: `Stooq:QuotePath` defaults to `q/d/l/?s={0}&i=d`, and `StooqCsvParser` understands
**both** response shapes — the single `Symbol,Date,Time,Open,High,Low,Close,Volume` row and the daily
history's `Date,Open,High,Low,Close,Volume` with one line per session, from which it reads the **newest**
row. So the moment the endpoint is reachable — from a network that is not challenged, if Stooq lifts the
check, or through any endpoint you point `Stooq:BaseAddress` at — the quote path works with no code change.
`IStockQuoteProvider` is the seam for swapping in a different provider entirely.

**Stooq answers HTTP 200 for every one of those cases**, so the status code cannot classify them and the
body has to. The mapping, which is what decides whether a participant sees a friendly answer or the red
banner:

| What arrived | Reported as | What the participant sees |
| --- | --- | --- |
| CSV with a `Close` column | `Quoted` | `AAPL.US quote is $93.42 per share` |
| `200` + body `Access denied` | `LookupFailed` | outage line + banner (see below) |
| CSV whose newest row is truncated | `LookupFailed` | the outage line + banner (never an older session's close) |
| `200` + an HTML page (verification or error) | `LookupFailed` | the outage line + banner |
| `4xx` / `5xx`, transport error, timeout, open circuit | `LookupFailed` | the outage line + banner |

The distinction matters: a mistyped ticker must not tell a participant that the whole service is down, and
a service outage must not look like a ticker that does not exist. The genuine unknown-symbol signal is
therefore **`N/D` inside a real CSV row** and nothing else.

`Access denied` was initially read as "symbol not found", because that is what it means inside a verified
browser session. Measuring it settled the question the other way: from any client outside such a session
Stooq returns that same body for a **valid** ticker as well as a misspelled one, so it carries no
information about the symbol. Treating it as "not found" would have answered *"Sorry, I could not find a
quote for AAPL.US"* for every correct ticker — a confident, wrong answer. It is a refusal, so it is reported
as one. When the answer is not a quote, the bot logs the media type and the body's first line, so which case
occurred is visible in the log rather than inferred.

Consequence for a reviewer: `/stock=aapl.us` exercises the graceful-failure path rather than the quote
path. The room gets `I could not reach the quote service, so I have no price for AAPL.US right now.`
posted by `Bot`, and the participant who asked also gets a red banner
(`The stock quote service (Stooq) is not responding right now. Please try again in a couple of minutes.`).
Nothing crashes, nothing is dead-lettered, and `/health` stays 200 on both hosts.

The success path is fully implemented and unit-tested against both documented CSV shapes:
`StooqCsvParser` locates the price by **header name** rather than column position, reads the newest row of
a multi-session history, treats a truncated newest row as a failed lookup rather than quoting an older
session, maps `N/D` to "symbol not found", and `StockQuoteAnswer.Quoted` produces exactly
`AAPL.US quote is $93.42 per share` (invariant culture, so a de-DE machine does not post `$93,42`).

---

## Bonus features

| Bonus | Status |
| --- | --- |
| .NET Identity authentication | **Done** |
| Bot handles unknown commands and exceptions gracefully | **Done** |
| Multiple chatrooms | **Done** |
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

**Multiple chatrooms — done** (bonus 2.2). The chat page renders a **Room** picker and a **New room** box:
pick another room to switch, or type a name to create one and land in it. Each room keeps its own history,
its own live posts and its own bot answers.

Most of it was already load-bearing before the feature existed, which is why it needed no schema change and
no migration: `ChatRoom` is a real aggregate with a database-unique name, `Messages` are keyed by
`ChatRoomId`, and `IChatNotifier` takes a `ChatRoomId` on every member and broadcasts to a per-room SignalR
group. What the bonus added is `Features/Rooms/CreateRoom` and `Features/Rooms/ListRooms`, two hub methods,
and the picker.

Three decisions worth naming, because each is a way this feature usually goes wrong:

- **Switching leaves the old SignalR group, and the server decides when.** The room a connection is in is
  kept in `HubCallerContext.Items` — one `Guid`, freed with the connection — so `JoinRoom` unsubscribes from
  the previous room itself. Asking the client to send a "leave" would work only for as long as the client
  is correct; forget it once and two rooms bleed into one window. Two integration tests assert the silence:
  after switching, the previous room's posts stop arriving.
- **A new room is announced to everyone, and that is a separate port.** `IChatNotifier` documents that it
  has no broadcast-to-everyone member, because a room's *traffic* must never reach connections that did not
  join it. The directory is not traffic — it is a name and an id every signed-in participant may see — so it
  travels over `IChatRoomNotifier` instead of weakening that guarantee. It is the one `Clients.All` in the
  application, and it fires when somebody creates a room, not when anybody types.
- **A duplicate name is an answer, not an exception.** The check runs on the *normalised* name, so
  `"  General  "` is caught by the lookup rather than by the unique index. One narrow race is accepted and
  documented in `CreateRoomHandler`: two participants submitting the same new name in the same instant can
  both pass the check, and the database — not the handler — rejects the loser.

The landing room is still resolved by name (`GetDefaultRoom`), deliberately: rooms are ordered by name, so
"the first room in the list" would have made a new room called `Alerts` everybody's landing room.

**Installer — not done** (bonus 2.5).

---

## Known issues

**The integration suite used to stall, and no longer does.** The symptom was that the tests reading the
message bus hung until their backstop, reporting `Test execution timed out` or `SendMessage did not answer
within 30 seconds`. It is fixed; the suite now runs 29/29 in 8–16 seconds, and the cause is worth recording
because the trap is easy to walk into with MassTransit's test harness.

`harness.Published` is a **live list, not a snapshot**. Enumerating it blocks in `Monitor.Wait` until the
harness decides nothing more is coming, which it only does when its inactivity timer fires. So any
assertion that must see the *end* of the sequence — anything reaching `ToList`, which includes
FluentAssertions' `ContainSingle` — waits for that timer by construction. Worse, when the timer fires while
the enumeration is in flight, MassTransit 8.5.10 deadlocks. Two thread stacks, captured with `dotnet-stack`
during a hang, show both halves: the test thread inside `AsyncElementList`'s enumerator holds its lock and
blocks in `CancellationTokenSource.WaitForCallbackToComplete`, while the timer thread runs
`AsyncInactivityObserver.NoActivity` into that same list's cancel callback and blocks on `Monitor.Enter`.
Neither side can advance.

The fix is `ChatServerFixture.PublishedAsync`, now the only way these tests read the bus. It waits with
`Any`, whose task completes as soon as a match arrives, on a cancellation token the test owns — so a wiring
mistake still fails in seconds — and then takes the first element, which is already present and returns
without waiting. Nothing enumerates to the end, so the timer is never involved. Cardinality moved to where
it can be proved deterministically: `RequestStockQuoteHandlerTests` asserts `Received(1)`.

Two earlier causes were found and fixed on the way to this one, both still worth knowing:
`AddMassTransitTestHarness` replaces MassTransit's hosted service, so building the host does **not** start
the bus and the fixture must call `harness.Start()`; and xUnit parallelises test *collections*, which put a
second in-memory bus beside the collection owning the SQL Server container, so `xunit.runner.json` runs the
assembly sequentially.

Two traps worth knowing while diagnosing: an interrupted run can leave a `testhost` process holding the
test DLLs, which makes the *build* fail with `MSB3027 … locked by testhost` — kill stray `testhost`
processes; and a `dotnet run` left over from a previous session will hold port 5271 or 5299.

---

## What is not finished

Stated plainly, per the challenge's request to say which parts were completed. All **mandatory**
requirements are implemented and verified end-to-end. Outstanding items, tracked in
[`docs/PLAN.md`](docs/PLAN.md):

| Task | Item |
| --- | --- |
| 1.18 | The manual end-to-end walkthrough recorded in `docs/ARCHITECTURE.md` |
| 2.3 | Hardening *tests* for malformed-payload dead-lettering and broker-restart recovery (the behaviour is implemented and measured; the two tests are not written) |
| 2.4 | Per-user posting rate limit (bonus) — considered, deliberately not built; see below |
| 2.5 | Installer (bonus) |

**Per-user rate limit — considered and deliberately left out.** It is named in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) §8 as both a security and a resource concern, so this is a
decision rather than an oversight, and the reasoning is worth stating.

The obvious implementation does not work here: ASP.NET Core's `AddRateLimiter` middleware counts **HTTP
requests**, and a SignalR connection is one request no matter how many messages travel over it. Only
`negotiate` would ever be throttled. A per-message limit therefore has to live where the messages are — a
`RateLimitBehavior` in the MediatR pipeline keyed on the authenticated user id, returning a failed `Result`
that the hub already knows how to deliver to the caller alone. That part would be small.

What stopped it is the part that is not small. An in-memory counter is per-instance, so it would be a second
place in the codebase whose correctness depends on there being exactly one host — and the scale-out limits
that already exist are documented rather than pretended away (§4 of `docs/ARCHITECTURE.md`). More
importantly, it is the only outstanding bonus that can **reject a legitimate message**: a wrong threshold
turns a working chat into one that silently drops what a participant typed. Shipping that untuned, days
before a review in which two people will type quickly at each other, trades a real risk for a checkbox.

The resource concern the challenge actually raises is addressed by other means, which are implemented and
measured: broker prefetch is bounded at 10, the outbound quote call has a 3-attempt budget and a 64 KiB
response ceiling, the history read is capped at 50 rows in SQL and cannot be widened by a client, and
broadcasts are per-room groups rather than `Clients.All`.

No screenshot is included: the UI is deliberately minimal, since the challenge states the backend is what
is evaluated.
