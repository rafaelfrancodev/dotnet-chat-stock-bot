# Delivery plan

Ordered, independently committable tasks. **Every mandatory requirement is finished before any bonus.**

Conventions for every task:
- One commit, imperative message.
- `dotnet build` + `dotnet test` green before the commit; `dotnet format` run.
- A task is DONE only when its listed unit tests exist and pass.

Legend: `[x]` done · `[ ]` pending.

---

## Phase 0 — Scaffold (done)

### [x] 0.1 Create the solution and enforce the dependency rule
Files: `Chat.sln`, `src/Chat.*/`, `tests/Chat.*/`
- 7 projects on `net10.0`, classic `.sln` format.
- References: Application→Domain, Infrastructure→Application, Web→Infrastructure, Bot→Infrastructure, UnitTests→Domain+Application+Infrastructure, IntegrationTests→Web+Bot.
- `Chat.Domain.csproj` contains **zero** package/project references.

### [x] 0.2 Add build and package governance
Files: `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`
- `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`, `EnforceCodeStyleInBuild=true`.
- Central package management; every version pinned in one file.
- MediatR pinned to 12.5.0 and FluentAssertions to 7.2.2 (last permissively licensed releases).
- **Verified:** `dotnet restore` and `dotnet build` succeed with 0 warnings.

### [x] 0.3 Add local infrastructure and secret hygiene
Files: `docker-compose.dev.yml`, `.env.example`, `.gitignore`, `src/Chat.Web/appsettings.json`, `src/Chat.Bot/appsettings.json`
- SQL Server 2022 + RabbitMQ 4 (management UI) in one dev compose file, both with healthchecks, both published on `127.0.0.1` only, named volumes for data.
- Credentials come from `.env` (git-ignored) and the compose file fails fast (`:?`) if they are missing.
- `appsettings.json` ships placeholders only (empty connection string, empty broker user/password); real values via user-secrets or `ConnectionStrings__ChatDatabase` / `RabbitMq__*` environment variables.
- `.gitignore` covers `.env`, `appsettings.*.Local.json`, `*.user`, `secrets.json`.
- **Verified:** `docker compose -f docker-compose.dev.yml up -d` brings both containers to `healthy`; SQL Server accepts the `.env` credentials on `127.0.0.1:1433`.

### [x] 0.4 Add the shared kernel
Files: `src/Chat.Domain/Common/*`, `tests/Chat.UnitTests/Domain/Common/ResultTests.cs`
- `Result`, `Result<T>`, `Error`, `Entity<TId>`, `AggregateRoot<TId>`, `IDomainEvent`.
- Tests: `Success_WithValue_ExposesValue`, `Failure_WithError_CarriesErrorAndBlocksValueAccess`, `Failure_WithoutError_IsRejected`.

### [x] 0.5 Add CQRS abstractions and the pipeline
Files: `src/Chat.Application/Abstractions/Messaging/*`, `src/Chat.Application/Behaviors/*`, `src/Chat.Application/DependencyInjection.cs`
- `ICommand`, `ICommand<T>`, `IQuery<T>` + handler markers over MediatR.
- `LoggingBehavior`, `ValidationBehavior` (validation failures become failed `Result`s, never exceptions).
- `AddApplication()` registers MediatR, both behaviors and all validators.

### [x] 0.6 Define the messaging contracts and topology names
Files: `src/Chat.Application/Contracts/Messaging/*`
- `MessagingConstants` (receive endpoints `stock-quote-requests` / `stock-quote-responses`, prefetch, retry policy, MassTransit's `_error` / `_skipped` suffixes).
- `StockQuoteRequested`, `StockQuoteResolved`, `StockQuoteOutcome` — plain records, so `Chat.Application` takes no MassTransit dependency.

### [x] 0.7 Write the architecture and plan documents
Files: `docs/ARCHITECTURE.md`, `docs/PLAN.md`

### [x] 0.8 Add dependency health checks and a combined dev run
Files: `src/Chat.Infrastructure/HealthChecks/*`, `src/Chat.Infrastructure/Messaging/RabbitMqOptions.cs`, `src/Chat.Infrastructure/Stocks/StooqOptions.cs`, `src/Chat.Web/Program.cs`, `src/Chat.Bot/*`, `Chat.slnLaunch`, `scripts/run-dev.ps1`
- `SqlServerHealthCheck` (`SELECT 1`), `RabbitMqHealthCheck` (open a connection), `StooqHealthCheck` (probe the service root). Hand-written, no extra NuGet dependency. MassTransit adds `masstransit-bus` on top.
- `MapChatHealthChecks()` maps `/health`, `/health/ready` and `/health/live` identically in both hosts; shared JSON payload via `HealthReportSerializer`.
- Stooq is tagged `external` and reports `Degraded`, so a Stooq outage never makes the bot unready.
- `/health/live` runs no dependency probe — a broker outage must not trigger a process restart.
- Chat.Bot became a `Microsoft.NET.Sdk.Web` host solely to serve health probes; it still has no persistence and no chat surface.
- `Chat.slnLaunch` gives Visual Studio a "Chat.Web + Chat.Bot" startup profile; `scripts/run-dev.ps1` does the same from the CLI.
- **Verified:** with the compose stack up, Chat.Web reports `sql-server` + `rabbitmq` healthy (200) and Chat.Bot reports `rabbitmq` + `stooq` healthy (200); stopping the broker turns `/health/ready` into 503 while `/health/live` stays 200.
- Fixed two real defects found by running it: `InvariantGlobalization=true` broke `Microsoft.Data.SqlClient`, and `localhost` resolved to `::1` against IPv4-only published ports.

### [x] 0.9 Replace raw RabbitMQ.Client with MassTransit
Files: `Directory.Packages.props`, `src/Chat.Infrastructure/{DependencyInjection.cs,Chat.Infrastructure.csproj}`, `src/Chat.Application/Contracts/Messaging/MessagingConstants.cs`, `src/Chat.{Web,Bot}/Program.cs`, `docs/*`
- MassTransit pinned to **8.5.10** — the last Apache-2.0 release, with a native `net10.0` target. 9.x moved to a commercial licence (Massient, Inc.), same reasoning as the MediatR and FluentAssertions pins.
- `AddMessaging(configuration, registerConsumers)` configures the bus over RabbitMQ: kebab-case endpoint formatter, prefetch and retry from `MessagingConstants`, host-specific consumer registration passed in by each host.
- `MessagingConstants` drops the exchange/routing-key/DLX names — MassTransit owns exchange layout and `_error` / `_skipped` queues — and keeps endpoint names, prefetch and the retry policy.
- Wire contracts remain plain records; `Chat.Application` still has no messaging framework dependency.
- **Measured:** with the broker stopped, MassTransit's `masstransit-bus` check stays `Healthy` for 60 s+ and logs no connection attempt, because a bus with no receive endpoints never opens one. `RabbitMqHealthCheck` is therefore kept alongside it; removal trigger recorded in task 1.10.
- **Verified:** both hosts healthy (200) with everything up; broker stopped → `/health/ready` 503 and `/health/live` 200 on both; broker restarted → both back to 200 without a restart.

---

## Phase 1 — Mandatory features

### [x] 1.1 Model the message value objects
Files: `src/Chat.Domain/Messages/{MessageContent,MessageAuthor,MessageId,MessageOrigin}.cs`, `src/Chat.Domain/ChatRooms/ChatRoomId.cs`
Acceptance:
- `MessageContent.Create` trims, rejects empty/whitespace, rejects `> 500` chars, returns `Result<MessageContent>`.
- `MessageAuthor.Create` rejects empty user id / display name; `MessageAuthor.Bot` is a well-known instance.
- `ChatRoomId` / `MessageId` are `readonly record struct` over `Guid` with `New()`.
Unit tests (`tests/Chat.UnitTests/Domain/Messages/`):
- `Create_EmptyContent_ReturnsFailure`, `Create_WhitespaceOnly_ReturnsFailure`, `Create_TooLong_ReturnsFailure`, `Create_ValidContent_TrimsAndSucceeds`, `Equality_SameValue_AreEqual`.

### [x] 1.2 Model the StockCode value object
Files: `src/Chat.Domain/StockCommands/StockCode.cs`
Acceptance:
- Normalises to lower case and trims; validates `^[a-z0-9.\-]{1,20}$`; `Display` returns upper case.
- Implemented as `StockCode.MaxLength` (length check) + a `[GeneratedRegex]` character allow-list
  anchored with `\A`/`\z` (in .NET `$` also matches before a trailing newline). Casing is
  `ToLowerInvariant`/`ToUpperInvariant` — culture-sensitive casing would break tickers under tr-TR.
- Rejects empty, `> 20` chars, spaces, and any character usable for URL/parameter injection (`&`, `?`, `/`, `=`, `%`, `#`).
Unit tests (`tests/Chat.UnitTests/Domain/StockCommands/StockCodeTests.cs`):
- `Create_MixedCase_NormalisesToLowerCase`, `Create_Empty_ReturnsFailure`, `Create_TooLong_ReturnsFailure`, `Create_ContainsUrlInjectionCharacters_ReturnsFailure` (`[Theory]`), `Display_ValidCode_ReturnsUpperCase`.

### [x] 1.3 Implement the chat command parser
Files: `src/Chat.Domain/StockCommands/{ChatCommandParser,ParsedChatInput}.cs`
Acceptance:
- `/stock=aapl.us` → `StockQuote(StockCode)`; case-insensitive command name.
- `/stock=` and `/stock` → `Invalid` / `UnknownCommand`; `/help` → `UnknownCommand("help")`.
- Text not starting with `/` → `PlainMessage`; leading/trailing whitespace tolerated.
- Never throws for any input.
- `ParsedChatInput` is a closed hierarchy (abstract record + private constructor + four nested sealed
  records), so task 1.9 branches with a type `switch` — no string matching, no casts.
- Ticker rules are not restated: a bad argument returns `Invalid` carrying `StockCode`'s own `Error`.
  Only "a slash with no command name" (`/`, `/=`) uses a parser-owned error.
- `StringComparison.OrdinalIgnoreCase` for the command name and `ToLowerInvariant` for the reported
  one — culture-sensitive comparison would change which commands match under tr-TR.
- `PlainMessage.Text` is trimmed (same normalisation `MessageContent.Create` applies).
Unit tests (`tests/Chat.UnitTests/Domain/StockCommands/ChatCommandParserTests.cs`):
- `Parse_StockCommand_ReturnsStockQuote`, `Parse_UpperCaseStockCommand_ReturnsStockQuote`, `Parse_StockCommandWithoutCode_ReturnsInvalid`, `Parse_UnknownSlashCommand_ReturnsUnknownCommand`, `Parse_PlainText_ReturnsPlainMessage`, `Parse_GarbageInput_DoesNotThrow` (`[Theory]`).

### [x] 1.4 Model the Message aggregate
Files: `src/Chat.Domain/Messages/{Message.cs,MessagePosted.cs}`
Acceptance:
- `Message.PostByParticipant(...)` and `Message.PostByBot(...)` are the only ways to create a message; constructor is private.
- Both raise `MessagePosted`; `Origin` is set correctly; `ChatRoomId` is stored by id (no navigation property).
- Factories return `Result<Message>` because three invariants can still fail once the value objects are
  valid: a `default` `ChatRoomId` (a struct, so an orphan post is representable), a `default`
  `PostedAtUtc` (it is the ordering key of the "last 50" query), and `MessageAuthor.Bot` passed to
  `PostByParticipant` (`Origin` and `Author` must agree). Null value objects are a programmer error, not
  an expected failure, so they throw `ArgumentNullException`.
- `PostByBot` takes no author: it uses `MessageAuthor.Bot` directly, so "the post owner is the bot" is
  structurally guaranteed rather than left to the caller.
- No clock in the domain: `postedAtUtc` is a `DateTimeOffset` parameter (task 1.6's `IDateTimeProvider`),
  normalised with `ToUniversalTime()` so the ordering key cannot drift with the caller's offset.
- `MessagePosted(MessageId, ChatRoomId, MessageAuthor, MessageContent, OccurredAtUtc)` — the room id is
  carried because the broadcast needs to know which SignalR group to target; `OccurredAtUtc` is the post
  instant, not a second clock reading.
- EF materialisation: a private parameterless constructor exists solely for EF Core (owned value objects
  cannot be bound through a parameterised constructor). See the note in task 1.7.
Unit tests (`tests/Chat.UnitTests/Domain/Messages/MessageTests.cs`, 14 cases):
- `PostByParticipant_ValidInput_RaisesMessagePosted`, `PostByParticipant_ValidInput_DoesNotProduceABotAuthor`, `PostByParticipant_BotAuthor_ReturnsFailure`.
- `PostByBot_ValidInput_SetsBotAuthorAndOrigin`.
- `Post_ValidInput_RaisesExactlyOneEventCarryingIdsAndContent` (`[Theory]`), `ClearDomainEvents_AfterPost_RemovesTheRecordedEvent`.
- `Post_DefaultChatRoomId_ReturnsFailure` (`[Theory]`), `Post_DefaultPostTime_ReturnsFailure` (`[Theory]`), `Post_NonUtcPostTime_NormalisesToUtcWithoutChangingTheInstant`, `Post_TwoMessages_GetDistinctIdentities`, `Constructors_AreAllNonPublic_SoTheFactoriesAreTheOnlyEntryPoint`.

### [x] 1.5 Model the ChatRoom aggregate
Files: `src/Chat.Domain/ChatRooms/{ChatRoom.cs,RoomName.cs,ChatRoomCreated.cs}`
Acceptance:
- `RoomName.Create` trims, collapses whitespace, rejects empty and `> 60` chars (`RoomName.MaxLength`).
- `ChatRoom.Create(RoomName, DateTimeOffset)` raises `ChatRoomCreated`.
- **`ChatRoom` holds no collection of messages.** A post is its own aggregate referencing the room by
  `ChatRoomId`; a navigation collection would make the "last 50" query load an unbounded history and
  would put two aggregates that never change together inside one consistency boundary. A reflection
  test pins it.
- Normalisation runs **before** the length check, exactly as `MessageContent` trims before measuring:
  the limit applies to what is stored and displayed, so a name that only exceeded it because of
  duplicated spaces is accepted rather than rejected for invisible input.
- Whitespace collapsing is a hand-written pass, not a `Regex`: `NeedsCollapsing` scans the trimmed
  string and an already-normalised name is returned as-is, so the common path allocates nothing and
  only a name that must be rewritten reaches the `StringBuilder`. All Unicode whitespace counts
  (`char.IsWhiteSpace`), so tabs, newlines and U+00A0 cannot smuggle layout into a room name.
- Same `Result`-vs-throw split as 1.4: a `default` `createdAtUtc` is an expected failure
  (`Errors.MissingCreationTime`), a null `RoomName` is a programmer error (`ArgumentNullException`).
  No clock in the domain — the caller supplies the instant and it is normalised with `ToUniversalTime()`.
- `ChatRoomCreated(ChatRoomId, RoomName, OccurredAtUtc)` mirrors `MessagePosted`'s shape.
- EF materialisation: a private parameterless constructor exists solely for EF Core, as on `Message`.
Unit tests (`tests/Chat.UnitTests/Domain/ChatRooms/{RoomNameTests,ChatRoomTests}.cs`, 28 cases):
- `Create_EmptyName_ReturnsFailure`, `Create_WhitespaceOnly_ReturnsFailure` (`[Theory]`), `Create_TooLong_ReturnsFailure`, `Create_ExactlyMaxLength_Succeeds`, `Create_LengthExceededOnlyByCollapsibleWhitespace_NormalisesAndSucceeds`.
- `Create_InternalWhitespace_CollapsesToSingleSpaces` (`[Theory]`), `Create_NonBreakingSpace_IsCollapsedLikeAnyOtherWhitespace`, `Create_SurroundingWhitespace_TrimsAndSucceeds`, `Equality_DifferentlySpacedInput_AreEqual`, `Equality_DifferentValue_AreNotEqual`, `Equality_DifferentCasing_AreNotEqual`, `ToString_ValidName_ReturnsNormalisedText`.
- `Create_ValidName_RaisesChatRoomCreated`, `Create_ValidName_RaisesExactlyOneEventCarryingIdAndName`, `Create_NullName_Throws`, `Create_DefaultCreationTime_ReturnsFailure`, `Create_NonUtcCreationTime_NormalisesToUtcWithoutChangingTheInstant`, `Create_TwoRooms_GetDistinctIdentities`, `ClearDomainEvents_AfterCreate_RemovesTheRecordedEvent`, `Constructors_AreAllNonPublic_SoTheFactoryIsTheOnlyEntryPoint`, `ChatRoom_HoldsNoMessages_SoTheAggregateBoundaryIsPreserved`.

### [x] 1.6 Declare the Application abstractions
Files: `src/Chat.Application/Abstractions/{Persistence,Realtime,Stocks,Time}/*`, `src/Chat.Application/Contracts/Messages/MessageDto.cs`
Acceptance:
- `IMessageRepository`, `IChatRoomRepository`, `IUnitOfWork`, `IChatNotifier`, `IStockQuoteRequester`, `IStockQuoteResponder`, `IStockQuoteProvider`, `IDateTimeProvider`.
- Every method is async and takes a `CancellationToken`; read methods return DTOs, not entities.
- XML doc comments on all of them.
Decisions taken here (later tasks must conform):
- **`MessageDto` lives in `Contracts/Messages/`, not in the 1.8 feature folder.** Three ports share it
  (`IMessageRepository.GetLatestAsync`, `IChatNotifier.BroadcastMessageAsync`, and 1.8's query result);
  putting it under one feature would make the other two depend on that feature. It carries no `UserId` —
  the chat window needs a display name, not everyone's authentication id.
- **Two documented exceptions to "every method is async"**: `IMessageRepository.Add` /
  `IChatRoomRepository.Add` stage an in-memory change and do no I/O (and 1.9's
  `repository.DidNotReceive().Add(...)` test depends on that shape), and `IDateTimeProvider.UtcNow` is a
  property. Everything else returns `Task`/`Task<T>` with a trailing `CancellationToken`.
- **Repositories never commit.** `Add` stages; `IUnitOfWork.SaveChangesAsync` commits exactly once per
  use case. This is what makes "this path performs no write" provable in a handler test.
- `IChatNotifier` takes a `ChatRoomId` on every member, so broadcasting to all connections is not
  expressible — the room group is structural, not a convention.
- `IStockQuoteProvider` returns `StockQuoteLookup(StockQuoteOutcome, decimal?)`, reusing the existing
  outcome enum rather than a second vocabulary, and must never throw for an unknown symbol or timeout.
- `IMessageRepository.GetLatestAsync` defaults `count` to `MessageConstants.LatestMessagesCount` and is
  documented to return oldest→newest, so callers never re-sort.
Unit tests (`tests/Chat.UnitTests/Application/AbstractionsTests.cs`, 26 cases) — the plan said "no tests",
but the conventions above are worth enforcing rather than documenting:
- `Port_AsynchronousMethods_ReturnATaskType`, `Port_AsynchronousMethods_TakeACancellationTokenLast`,
  `Port_NoMember_LeaksADomainEntityOrAnIQueryable` (all `[Theory]` over the eight ports).
- `Ports_ExposeTheAsynchronousSurfaceTheRulesAreMeantToCover` — guards the three rules above against
  passing vacuously if reflection ever stops finding the members.
- `Application_ReferencesNoInfrastructureFramework` — asserts the compiled assembly references no EF Core,
  MassTransit, ASP.NET Core or RabbitMQ.Client, so the dependency rule is checked by the build.

### [ ] 1.7 Add persistence (EF Core + Identity) and the initial migration
Files: `src/Chat.Infrastructure/Persistence/{ChatDbContext.cs,Configurations/*,Migrations/*}`, `src/Chat.Infrastructure/Identity/ApplicationUser.cs`, `src/Chat.Infrastructure/DependencyInjection.cs`
Acceptance:
- `ChatDbContext : IdentityDbContext<ApplicationUser>` with value converters for the strongly-typed ids and value objects.
- `Message` is materialised through its private parameterless constructor (added in 1.4) and its
  properties are `private init`, so the mapping must leave the default backing-field access mode in
  place — no `UsePropertyAccessMode(PropertyAccessMode.Property)` on `Author`, `Content`, `Origin`,
  `PostedAtUtc` or `ChatRoomId`. `MessageAuthor` maps as an owned/complex type (`UserId`,
  `DisplayName`), `MessageContent` through a `string` converter.
- `ChatRoom` is materialised the same way (private parameterless constructor from 1.5, `private init`
  properties): `RoomName` maps through a `string` converter and the default backing-field access mode
  must stay in place. Room name uniqueness is a database concern — the aggregate cannot see its peers.
- Composite index `IX_Messages_ChatRoomId_PostedAtUtc`; unique index on `ChatRooms.Name` (case
  sensitivity follows the column collation; `RoomName` itself is case-preserving and case-sensitive).
- `AddPersistence` registers SQL Server from `ConnectionStrings:ChatDatabase` (with `EnableRetryOnFailure` for container startup races), the repositories and `IUnitOfWork`. A missing connection string fails fast at startup with a clear message.
- `dotnet ef migrations add InitialCreate` committed; `dotnet ef database update` creates `ChatDb` and succeeds from a clean checkout against the compose container.
Unit tests: none (covered by 1.16 integration tests); the migration must be verified manually.

### [ ] 1.8 Implement GetLatestMessages
Files: `src/Chat.Application/Features/Messages/GetLatestMessages/*`, `src/Chat.Infrastructure/Persistence/Repositories/MessageRepository.cs`
Acceptance:
- Query defaults to `MessageConstants.LatestMessagesCount` (50).
- Repository uses `AsNoTracking`, `OrderByDescending(PostedAtUtc)`, `Take(count)`, projects to `MessageDto` in SQL, then reverses in memory.
- `MessageDto` already exists from 1.6 in `Chat.Application/Contracts/Messages/` — reuse it, do not
  declare a second one in the feature folder.
Unit tests (`tests/Chat.UnitTests/Application/Features/Messages/`):
- `Handle_RoomWithMessages_ReturnsOldestToNewest`, `Handle_MoreThan50Messages_ReturnsOnly50`, `Handle_UnknownRoom_ReturnsFailure`.

### [ ] 1.9 Implement PostMessage with the stock-command branch
Files: `src/Chat.Application/Features/Messages/PostMessage/*`, `src/Chat.Application/Features/StockCommands/RequestStockQuote/*`
Acceptance:
- `PostMessageHandler` parses first: plain message → persist + notify; stock command → dispatch `RequestStockQuoteCommand`, **no repository call**; unknown command → failed `Result`, nothing persisted or published.
- `RequestStockQuoteHandler` has no repository/`IUnitOfWork` dependency and publishes through `IStockQuoteRequester`.
- `PostMessageValidator` rejects an empty room id and empty raw input.
Unit tests (highest-value tests in the repo):
- `Handle_PlainMessage_PersistsAndNotifies`
- `Handle_StockCommand_DoesNotPersistMessage` (asserts `repository.DidNotReceive().Add(...)`)
- `Handle_StockCommand_PublishesStockQuoteRequest`
- `Handle_UnknownCommand_ReturnsFailureAndPersistsNothing`
- `Handle_UnknownRoom_ReturnsFailure`

### [ ] 1.10 Wire the MassTransit publishers and receive endpoints
Files: `src/Chat.Infrastructure/Messaging/*`, `src/Chat.Application/Abstractions/Messaging/*`
Acceptance:
- `IStockQuoteRequester` / `IStockQuoteResponder` implemented over `IPublishEndpoint`, so Application still depends on its own abstractions and never on MassTransit.
- Receive endpoints named from `MessagingConstants` (`stock-quote-requests`, `stock-quote-responses`); each host passes only its own consumers into `AddMessaging(...)`.
- Retry policy `Interval(RetryLimit, RetryIntervalSeconds)`; exhausted messages land in `<queue>_error`, never requeue forever.
- Credentials come from user-secrets/env, never `appsettings.json`.
- **Re-measure the broker health checks now that receive endpoints exist:** stop RabbitMQ and confirm `masstransit-bus` reports unhealthy. If it does, delete `RabbitMqHealthCheck` + `AddChatBroker` and rely on the bus check alone (it costs nothing; ours opens a connection per probe). If it does not, keep ours and record the measurement.
Unit tests: `Serializer_RoundTrip_PreservesContract` for both contracts; publisher tests using MassTransit's `ITestHarness` in-memory transport (`Publish_StockQuoteRequested_IsSentToBus`) — no broker required.

### [ ] 1.11 Add Identity, authentication and the seeded default room to Chat.Web
Files: `src/Chat.Web/Program.cs`, `src/Chat.Web/Areas/Identity/*`, `src/Chat.Infrastructure/Persistence/ChatDbSeeder.cs`
Acceptance:
- Register/login/logout work through the default Identity UI; cookie is HttpOnly + SameSite=Lax + Secure.
- Default password policy and lockout untouched.
- Migrations applied and a `General` room seeded at startup.
- Anonymous users are redirected to login when opening the chat page.
Unit tests: none; covered by 1.16.

### [ ] 1.12 Implement ChatHub and the SignalR notifier
Files: `src/Chat.Web/Hubs/ChatHub.cs`, `src/Chat.Web/Realtime/SignalRChatNotifier.cs`
Acceptance:
- `[Authorize]` on the hub. Author id and display name come from `Context.User`; the client payload carries only `roomId` and `text`.
- `JoinRoom` adds the connection to `Groups`, `OnDisconnectedAsync` cleans up.
- Broadcasts go to `Clients.Group(...)`, never `Clients.All`.
- Hub methods are ~5 lines: claims → `ISender.Send` → map `Result` (errors go to `Clients.Caller` only).
Unit tests: `SendMessage_UsesClaimsIdentity_NotClientPayload` (hub tested with a substituted `HubCallerContext`).

### [ ] 1.13 Add the minimal chat page
Files: `src/Chat.Web/Pages/Chat.cshtml(.cs)`, `src/Chat.Web/wwwroot/js/chat.js`
Acceptance:
- Loads the last 50 messages oldest→newest, then appends live ones.
- Vanilla JS + the SignalR client; messages rendered with `textContent` (no `innerHTML`, no `Html.Raw`).
- No SPA framework, no build step.
Unit tests: none (UI). Manual check: two browsers, two users, both see each other's messages.

### [ ] 1.14 Add the Stooq client and CSV parser
Files: `src/Chat.Infrastructure/Stocks/{StooqOptions,StooqClient,StooqCsvParser}.cs`
Acceptance:
- Typed `HttpClient` via `IHttpClientFactory`, 10 s timeout, standard resilience handler (retry + circuit breaker).
- URL built only from a validated `StockCode`.
- Parser handles: valid row → price from the `Close` column; `N/D` fields → `SymbolNotFound`; missing/short/garbage CSV → `LookupFailed`; invariant-culture decimal parsing.
Unit tests (`tests/Chat.UnitTests/Infrastructure/Stocks/StooqCsvParserTests.cs`):
- `Parse_ValidRow_ReturnsClosePrice`
- `Parse_NotAvailableRow_ReturnsSymbolNotFound`
- `Parse_HeaderOnly_ReturnsLookupFailed`
- `Parse_MalformedRow_ReturnsLookupFailed` (`[Theory]`)
- `Parse_CommaDecimalCulture_StillParsesInvariant`

### [ ] 1.15 Implement the bot use case and worker
Files: `src/Chat.Application/Features/StockCommands/ResolveStockQuote/*`, `src/Chat.Bot/StockQuoteRequestConsumer.cs`, `src/Chat.Bot/Program.cs`
Acceptance:
- `ResolveStockQuoteHandler` calls `IStockQuoteProvider`, formats the message and publishes `StockQuoteResolved` through `IStockQuoteResponder`.
- Exact wording on success: `"AAPL.US quote is $93.42 per share"` (price formatted with 2 decimals, invariant culture).
- Unknown symbol and lookup failure produce friendly messages and `Outcome != Quoted`; the handler never throws.
- The worker is a `BackgroundService`, fully async, honours `stoppingToken`.
Unit tests:
- `Handle_ValidQuote_PublishesExpectedMessageFormat`
- `Handle_SymbolNotFound_PublishesFriendlyMessage`
- `Handle_ProviderThrows_PublishesLookupFailedAndDoesNotRethrow`

### [ ] 1.16 Consume quote responses in Chat.Web and post them as the bot
Files: `src/Chat.Web/Messaging/StockQuoteResponseConsumer.cs`, `src/Chat.Application/Features/Messages/PostBotMessage/*`
Acceptance:
- Consumer deserialises `StockQuoteResolved` and sends `PostBotMessageCommand`.
- `PostBotMessageHandler` creates a `Message` via `Message.PostByBot`, persists it and broadcasts to the room group.
- Unparseable payloads are dead-lettered, not requeued.
Unit tests: `Handle_BotMessage_PersistsWithBotAuthorAndBroadcasts`, `Handle_UnknownRoom_ReturnsFailureWithoutBroadcast`.

### [ ] 1.17 Add the integration test suite
Files: `tests/Chat.IntegrationTests/{CustomWebApplicationFactory.cs,...}`
Acceptance:
- Factory starts a throwaway SQL Server container via `Testcontainers.MsSql` (same provider as production), applies migrations once per collection, substitutes `IStockQuoteRequester`, and provides a test-auth helper.
- Tests are skipped with a clear message when Docker is unavailable, so `dotnet test` never fails for environmental reasons.
- Covers: anonymous hub connection rejected; register→login→chat page reachable; posting a message then reading it back in order and capped at 50; `/stock=aapl.us` publishes a broker request **and creates no message row**; two hub clients in the same room see each other's messages.
- Deterministic waits (`TaskCompletionSource` + timeout), no `Task.Delay`.

### [ ] 1.18 Verify the end-to-end flow manually and record it
Files: `docs/ARCHITECTURE.md` (adjust if reality differs)
Acceptance:
- `docker compose up -d`, `dotnet run` both hosts, two browsers with two users: chat works, `/stock=aapl.us` produces the bot post, the command itself is absent from the database (verified with a SQL query against `Messages`).

---

## Phase 2 — Bonus features

### [ ] 2.1 Confirm and document the .NET Identity bonus
Files: `README.md`
- Identity was delivered in 1.11 as the mechanism for the mandatory "registered users log in". Record it as a completed bonus, honestly.

### [ ] 2.2 Multiple chat rooms
Files: `src/Chat.Application/Features/Rooms/{CreateRoom,ListRooms}/*`, `src/Chat.Web/Pages/Chat.cshtml(.cs)`, `src/Chat.Web/Hubs/ChatHub.cs`
Acceptance:
- Create a room (unique name, validated), list rooms, switch rooms in the UI.
- Switching leaves the old SignalR group and joins the new one; history reloads for the selected room.
- A stock quote requested in room A is posted only to room A.
Unit tests: `Handle_DuplicateName_ReturnsFailure`, `Handle_ValidName_CreatesRoom`, `Handle_NoRooms_ReturnsEmptyList`.

### [ ] 2.3 Harden the bot against unknown commands and exceptions
Files: `src/Chat.Bot/StockQuoteRequestConsumer.cs`, `src/Chat.Application/Features/StockCommands/ResolveStockQuote/*`
Acceptance:
- Malformed JSON → logged + dead-lettered, consumer stays alive.
- Stooq timeout / 5xx / HTML error page → friendly `LookupFailed` message in the room.
- Unknown chat commands answered privately to the caller (from 1.9) — verified end to end.
- The bot process survives a broker restart (automatic recovery) — verified manually.
Unit tests: `Consume_MalformedPayload_NacksWithoutRequeue`, `Consume_HandlerThrows_DoesNotStopConsumer`.

### [ ] 2.4 Rate-limit posting per user
Files: `src/Chat.Web/Hubs/ChatHub.cs` (or a hub filter)
Acceptance: a user exceeding N messages per window gets a caller-only error; the limit is a named constant; no unbounded per-connection state.
Unit tests: `SendMessage_AboveRateLimit_ReturnsFailure`.

### [ ] 2.5 Installer
Files: `installer/` (`install.ps1` + `install.sh`), `README.md`
Acceptance:
- One script: checks prerequisites, copies `.env.example`, starts RabbitMQ, applies migrations, builds, prints the two `dotnet run` commands.
- Idempotent and safe to re-run; fails with a clear message when Docker or the SDK is missing.

---

## Phase 3 — Documentation and delivery

### [ ] 3.1 Write the README (graded deliverable)
Files: `README.md`
- What it is, screenshot, prerequisites, exact run steps (verified on a clean clone), architecture summary with a link to `docs/ARCHITECTURE.md`, design decisions (bot messages persisted, SQL Server in Docker, contracts location, licence pins), bonus checklist with honest status, how to run the tests.

### [ ] 3.2 Final review pass
- Dependency rule re-checked; no secrets (`grep -riE "password|secret|apikey" src/`); all hubs `[Authorize]`; identity from claims; stock code validated before the outbound call; `dotnet format` clean; `dotnet build` 0 warnings; `dotnet test` green.

### [ ] 3.3 Sync CLAUDE.md and prepare the deliverable
- Update the Architecture / Commands / Status / Conventions / Gotchas sections to match reality.
- Confirm the local Git history is meaningful (one commit per task) and that `.git/` is included in the delivery.
