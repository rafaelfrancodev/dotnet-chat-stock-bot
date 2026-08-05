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

### [x] 1.10a Scope handler registration per host so both processes start
Files: `src/Chat.Application/Abstractions/Hosting/{IWebFeature,IBotFeature}.cs`, `src/Chat.Application/DependencyInjection.cs`, the three existing handlers, `src/Chat.Web/{Program.cs,Hubs/ChatHub.cs,Realtime/SignalRChatNotifier.cs}`, `src/Chat.Bot/Program.cs`

Fixes a defect introduced in 1.9: **neither host could start.**
- `AddApplication()` scanned the whole assembly, so MediatR registered every handler in both processes.
  Chat.Bot was asked to construct `PostMessageHandler` / `GetLatestMessagesHandler`, which need the
  `IChatRoomRepository` it deliberately does not have, and `RequestStockQuoteHandler`, which needs a clock
  it had not registered. Chat.Web failed separately because `IChatNotifier` had no implementation yet.
- `AddApplication<TFeature>()` now filters the scan by a host marker — `IWebFeature` or `IBotFeature`.
  Every handler declares which process runs it, so the bot's lack of database access stays structural
  rather than accidental: marking a persistence-dependent handler `IBotFeature` fails at startup.
- Chat.Bot additionally calls `AddSystemClock()`.
- `SignalRChatNotifier` (over `IHubContext<ChatHub>`) implements `IChatNotifier` in Chat.Web,
  group-scoped, never `Clients.All`. **`ChatHub` is deliberately not mapped yet** — mapping it before
  authentication exists (1.11) would expose an unauthenticated realtime surface. Task 1.12 adds
  `[Authorize]`, `JoinRoom`, `SendMessage` and the `MapHub` call.

Unit tests (`tests/Chat.UnitTests/Application/HostFeatureTests.cs`, 4 cases):
- `EveryHandler_DeclaresExactlyOneHostThatRunsIt` — a handler that forgets its marker is registered by
  nobody and surfaces at runtime as "no handler for request"; this makes it a build failure instead.
- `BotHost_RegistersNoHandlerThatNeedsPersistenceOrTheChatSurface`, `WebHost_RegistersOnlyItsOwnHandlers`,
  `BotHost_RegistersOnlyItsOwnHandlers` (asserted as an absence, because the bot correctly has no
  handlers until 1.15).

**Verified:** both hosts reach `/health/live` 200 with zero unhandled exceptions.

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

### [x] 1.7 Add persistence (EF Core + Identity) and the initial migration
Files: `src/Chat.Infrastructure/Persistence/{ChatDbContext.cs,PersistenceConstants.cs,Converters/*,Configurations/*,Repositories/*,Migrations/*}`, `src/Chat.Infrastructure/Identity/ApplicationUser.cs`, `src/Chat.Infrastructure/Time/SystemDateTimeProvider.cs`, `src/Chat.Infrastructure/DependencyInjection.cs`, `src/Chat.Web/Program.cs`, `.editorconfig`
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
Decisions taken here (later tasks must conform):
- **`Messages` has no foreign keys at all.** `AuthorUserId` is a plain `nvarchar(450)` — the bot's
  `system:bot` is not an Identity user, so an FK to `AspNetUsers` would reject every quote answer the
  challenge requires. `ChatRoomId` is a cross-aggregate reference validated by `ExistsAsync`, so an
  unknown room is an expected `Result` failure rather than a `DbUpdateException`.
- Column widths: `AuthorUserId` `nvarchar(450)` (Identity's own `AspNetUsers.Id` width, the widest key
  SQL Server indexes), `AuthorDisplayName` and `ApplicationUser.DisplayName` `nvarchar(256)` (Identity's
  `UserName` width, and display names come from there), `Content` `nvarchar(500)`
  (`MessageConstants.MaxContentLength`), `Name` `nvarchar(60)` (`RoomName.MaxLength`). Nothing is
  `nvarchar(max)`. All in `PersistenceConstants`.
- `MessageAuthor` maps as an EF **complex type** (not `OwnsOne`): no entity identity, no tracking
  overhead, and `message.Author.DisplayName` stays translatable in the read projection.
- Timestamps are `datetime2(7)` via `UtcDateTimeOffsetConverter`, which drops the always-zero offset on
  write and restores `DateTimeKind.Utc` on read — a local time cannot enter the ordering key.
- **The read projection stops at the value objects, not at `MessageDto`.** EF Core cannot translate
  member access on a value-converted type (`message.Content.Value`), so the query selects five columns
  into an internal `LatestMessageRow` and one loop both reverses the list and unwraps it. Verified SQL:
  `SELECT TOP(@p) [Id], [AuthorDisplayName], [Content], [PostedAtUtc], [Origin] FROM [Messages] WHERE
  [ChatRoomId] = @chatRoomId ORDER BY [PostedAtUtc] DESC, [Id] DESC`.
- `IDateTimeProvider` is implemented here as `SystemDateTimeProvider` and registered by
  `AddSystemClock()` — a separate extension, because the clock is not persistence and `Chat.Bot` must
  never call `AddPersistence()`. `Chat.Web` calls it today; 1.15 should call it if the bot needs a clock.
- `AddPersistence` **throws** on a missing connection string (a host that cannot reach its database
  cannot serve one request), while `AddChatDatabase` still *reports* the same gap on `/health` — a host
  may register the probe without the persistence layer. Both now read `PersistenceConstants.ConnectionStringName`.
- `.editorconfig` marks `src/Chat.Infrastructure/Persistence/Migrations/*.cs` as `generated_code = true`:
  `dotnet ef` emits block-scoped namespaces and an unused `using System;`, which `TreatWarningsAsErrors`
  turned into IDE0161/IDE0005 build errors. Scoped to the generated folder; nothing else is relaxed.
- **Verified against the running container:** migration `20260804190713_InitialCreate` applied to `ChatDb`;
  `sys.foreign_keys` on `dbo.Messages` returns 0; `IX_Messages_ChatRoomId_PostedAtUtc` exists with
  `PostedAtUtc` descending; `IX_ChatRooms_Name` is unique; columns are `uniqueidentifier` / `nvarchar(500)` /
  `int` / `datetime2` / `nvarchar(256)` / `nvarchar(450)`. A throwaway round trip wrote a room, three
  participant posts and one **bot** post, read them back oldest→newest with all offsets zero, and was then
  deleted — proving materialisation through the private constructors and that no FK rejects `system:bot`.
Unit tests (`tests/Chat.UnitTests/Infrastructure/Persistence/`, 27 cases) — the plan said "none", but the
model and the generated SQL can be asserted with no database at all, which is worth more than prose:
- `ChatDbContextModelTests` (17): `Model_ForChatAndIdentity_BuildsWithoutThrowing`,
  `Messages_HaveNoForeignKey_SoTheBotCanOwnItsPosts`, `Messages_AreIndexedByRoomThenNewestPostFirst`,
  `ChatRooms_HaveAUniqueIndexOnName`, `StronglyTypedIds_AreStoredAsGuids` (`[Theory]`),
  `ValueObjects_AreStoredAsBoundedStrings` (`[Theory]`),
  `MessageAuthor_IsStoredInTheMessagesTable_AsTwoBoundedColumns`, `MessageOrigin_IsStoredAsAnInteger`,
  `Timestamps_AreStoredAsUtcInstantsWithoutAnOffset` (`[Theory]`),
  `ReadingATimestamp_RestoresUtcKindAndZeroOffset`, `Aggregates_DoNotPersistTheirDomainEvents`
  (`[Theory]`), `IdentityUsers_CarryABoundedDisplayName`.
- `MessageRepositoryQueryTests` (2): `LatestMessagesQuery_FiltersOrdersAndLimits_InTheDatabase`,
  `LatestMessagesQuery_SelectsOnlyTheColumnsTheChatWindowRenders` — compiled with `ToQueryString()`,
  which translates the query offline and would fail if the mapping ever stopped supporting it.
- `AddPersistenceTests` (8): `AddPersistence_WithoutAConnectionString_FailsFastWithAnActionableMessage`
  (`[Theory]`), `AddPersistence_WithAConnectionString_ResolvesThePersistencePorts` (`[Theory]`),
  `AddSystemClock_RegistersAClockThatReadsUtc`.

### [x] 1.8 Implement GetLatestMessages
Files: `src/Chat.Application/Features/Messages/GetLatestMessages/{GetLatestMessagesQuery,GetLatestMessagesHandler,GetLatestMessagesValidator}.cs`, `src/Chat.Application/Chat.Application.csproj`
Acceptance:
- `GetLatestMessagesQuery(ChatRoomId, int Count = MessageConstants.LatestMessagesCount)` implements
  `IQuery<IReadOnlyList<MessageDto>>`; the 50 is never restated as a literal.
- **`MessageRepository` already ships from 1.7** — `AsNoTracking`, ordering, `Take(count)` and the
  projection all run in SQL and the result is already oldest→newest. This task is the handler only: it
  checks the room exists and calls `GetLatestAsync`. Do not re-sort and do not re-implement the query.
- `MessageDto` already exists from 1.6 in `Chat.Application/Contracts/Messages/` — reuse it, do not
  declare a second one in the feature folder.
Decisions taken here (later tasks must conform):
- **`Count` is bounded at `MessageConstants.LatestMessagesCount`, not merely defaulted to it.** It
  reaches SQL as `TOP(n)`, so a caller-supplied count is a lever on how much the database reads and how
  much every chat window downloads — the resource consumption the challenge warns about. Capping it at
  the same constant also makes "show only the last 50" enforced: no client can ask for the 51st message.
  Lower bound `GetLatestMessagesValidator.MinimumCount = 1` (0 or less is a caller bug, not an empty room).
- **The room check runs first and short-circuits**, so an unknown room costs one `AnyAsync` and never a
  history read. The failure is `GetLatestMessagesQuery.Errors.ChatRoomNotFound` (`ChatRoom.NotFound`).
- **Failures are declared next to the request, not on the handler**, because the handler is `internal`:
  the request and the failures it can produce are the public surface of a feature. The "unknown room"
  failure was originally nested on this query; **1.9 promoted it to `Chat.Application/Errors/ChatRoomErrors.NotFound`**
  when the second use case needed it, so the code `ChatRoom.NotFound` is defined once. `GetLatestMessagesQuery.Errors`
  no longer exists; the handler and `GetLatestMessagesHandlerTests` use the shared class.
- **Handlers and validators are `internal sealed`**; `Chat.Application.csproj` gained
  `InternalsVisibleTo Chat.UnitTests` (same reason and shape as `Chat.Infrastructure`). MediatR and
  FluentValidation both find them by assembly scan (`includeInternalTypes: true` was already set), and
  two registration tests pin that, since a mistake would surface as a runtime resolution failure in the
  hub rather than a build error.
Unit tests (`tests/Chat.UnitTests/Application/Features/Messages/`, 23 cases):
- `GetLatestMessagesHandlerTests` (9): `Handle_RoomWithMessages_ReturnsOldestToNewest`,
  `Handle_RoomWithMessages_ReturnsTheRepositorySequenceUntouched` (same instance — proves nothing is
  re-sorted or re-paged), `Handle_MoreThan50Messages_ReturnsOnly50`, `Handle_UnknownRoom_ReturnsFailure`,
  `Handle_UnknownRoom_DoesNotQueryMessages`, `Handle_NoCountSupplied_RequestsTheDefaultCountFromTheRepository`,
  `Handle_ExplicitCount_IsPassedThroughToTheRepository`, `Handle_Always_ForwardsTheCancellationTokenToBothQueries`,
  `Handle_EmptyRoom_SucceedsWithAnEmptyList`.
- `GetLatestMessagesValidatorTests` (11): `Validate_DefaultQuery_IsValid`, `Validate_DefaultRoomId_IsRejected`,
  `Validate_NonPositiveCount_IsRejected` (`[Theory]`), `Validate_CountAboveTheCap_IsRejected` (`[Theory]`),
  `Validate_CountOnTheBoundary_IsAccepted` (`[Theory]`), `Validate_DefaultCount_EqualsTheChallengeLimit`.
- `GetLatestMessagesRegistrationTests` (3): `AddApplication_RegistersTheInternalQueryHandler`,
  `AddApplication_RegistersTheInternalValidator`,
  `Send_InvalidCount_IsRejectedByThePipelineAsAFailedResult` (validation failure → failed `Result`, not an exception).

### [x] 1.9 Implement PostMessage with the stock-command branch
Files: `src/Chat.Application/Features/Messages/PostMessage/{PostMessageCommand,PostMessageOutcome,PostMessageHandler,PostMessageValidator}.cs`, `src/Chat.Application/Features/StockCommands/RequestStockQuote/{RequestStockQuoteCommand,RequestStockQuoteHandler}.cs`, `src/Chat.Application/Errors/ChatRoomErrors.cs`
Acceptance:
- `PostMessageHandler` checks the room, then classifies: plain message → persist + notify; stock command → dispatch `RequestStockQuoteCommand`, **no repository call**; unknown command → failed `Result`; malformed command → the parser's own `Error`. Nothing but a plain message is ever persisted or broadcast.
- `RequestStockQuoteHandler` has no repository/`IUnitOfWork` dependency and publishes through `IStockQuoteRequester`.
- `PostMessageValidator` rejects an empty room id, empty/whitespace raw input, an over-long line and an empty author identity.
Decisions taken here (later tasks must conform):
- **"A stock command is never saved" is enforced by structure, in four layers, not by discipline:**
  (1) the input is classified by `ChatCommandParser` before anything else and the branch is a type
  `switch` over the closed `ParsedChatInput` hierarchy; (2) the only method that touches
  `IMessageRepository`/`IUnitOfWork` takes a `ParsedChatInput.PlainMessage`, so persisting a command
  would require turning a `StockQuote` into a plain message — the type system forbids it; (3)
  `RequestStockQuoteHandler`, the whole of the `/stock=` path, is constructed without any persistence
  port; (4) the `switch` ends in `UnreachableException`, so a fifth case fails loudly instead of falling
  into the persist path.
- **The stock branch goes through `ISender`, not straight to `IStockQuoteRequester`.** Publishing inline
  would have made `RequestStockQuoteHandler`'s "no persistence dependency" decorative — the guarantee is
  only worth something if the request path really is that handler. It also keeps both use cases
  independently dispatchable (1.12's hub, 2.2's rooms) and gives the stock command the same validation
  and logging pipeline as every other request. Cost: one nested mediator dispatch per `/stock=` line,
  negligible next to the broker round trip it precedes.
- **`RequestStockQuoteCommand` carries no request id and no timestamp.** The handler mints
  `Guid.CreateVersion7()` and reads `IDateTimeProvider`, so no caller can replay a correlation id or
  backdate a request. It carries a `StockCode`, not a `string`: an unvalidated ticker is not expressible.
- **Identity is a documented contract, not a hope.** `AuthorUserId`/`AuthorDisplayName` are filled by the
  hub from `Context.User`; the client payload carries only `ChatRoomId` and `RawInput`. The command has no
  origin flag, no author flag and no post time, so a caller cannot ask to post as somebody else, as the
  bot, or at a chosen instant. Recorded on the type itself so 1.12 cannot get it wrong.
- **The broadcast is done by the handler, on the line after `SaveChangesAsync`,** not by a domain-event
  dispatcher. Same ordering guarantee (nothing announced before it is committed) with one subscriber, in
  one use case, in two adjacent lines. `MessagePosted` is still raised, so a dispatcher stays additive.
  `ARCHITECTURE.md` §2.4 records the reasoning.
- **An unknown command is answered with fixed text.** `UnknownCommand.CommandName` is untrusted and
  unbounded, so it is never echoed into an error message; the hub returns the failure to `Clients.Caller`
  only (1.12).
- **`PostMessageOutcome` is an enum, not a response DTO.** The broadcast has already happened when the
  caller sees the result, so returning the post would invite a hub to render it twice.
- `GetLatestMessagesQuery.Errors.ChatRoomNotFound` was promoted to `Chat.Application/Errors/ChatRoomErrors.NotFound`
  (see the note in 1.8); 1.16 must reuse it rather than declare a third copy.
Unit tests (`tests/Chat.UnitTests/Application/Features/`, 62 cases — the highest-value tests in the repo):
- `PostMessageHandlerTests` (31): `Handle_PlainMessage_PersistsAndNotifies`,
  `Handle_PlainMessage_PersistsTheClaimsAuthorTheRoomAndTheClockInstant`,
  `Handle_PlainMessage_CommitsBeforeBroadcasting`, `Handle_PlainMessage_DoesNotPublishAStockRequest`,
  `Handle_StockCommand_DoesNotPersistMessage` (`[Theory]`, asserts `Add` **and** `SaveChangesAsync` were
  never called), `Handle_StockCommand_DoesNotBroadcastTheCommand`,
  `Handle_StockCommand_PublishesStockQuoteRequest`,
  `Handle_StockCommandDispatchFails_ReturnsThatFailureAndPersistsNothing`,
  `Handle_UnknownCommand_ReturnsFailureAndPersistsNothing` (`[Theory]`),
  `Handle_UnknownCommand_DoesNotEchoTheCommandNameBackToTheCaller`,
  `Handle_InvalidCommand_ReturnsTheParserErrorAndPersistsNothing` (`[Theory]`),
  `Handle_InvalidStockCommand_ReturnsTheStockCodeErrorInstance`, `Handle_UnknownRoom_ReturnsFailure`,
  `Handle_UnknownRoom_DoesNoWorkOnAnyBranch` (`[Theory]`), `Handle_SaveFails_DoesNotNotify`,
  `Handle_UnusableAuthor_ReturnsFailureWithoutPersistingOrNotifying` (`[Theory]`),
  `Handle_ContentTooLongForTheDomain_ReturnsFailureWithoutPersistingOrNotifying`,
  `Handle_DomainFactoryFails_DoesNotNotify`,
  `Handle_PlainMessage_ForwardsTheCancellationTokenToEveryCall`,
  `Handle_StockCommand_ForwardsTheCancellationTokenToTheDispatch`, `Handle_NullCommand_Throws`.
- `PostMessageValidatorTests` (17): `Validate_UsableInput_IsValid` (`[Theory]`),
  `Validate_DefaultRoomId_IsRejected`, `Validate_EmptyOrWhitespaceRawInput_IsRejected` (`[Theory]`),
  `Validate_RawInputAboveTheCap_IsRejected`, `Validate_RawInputOnTheBoundary_IsAccepted`,
  `Validate_MaximumLengthSurroundedByWhitespace_IsAccepted`, `Validate_EmptyAuthorUserId_IsRejected`
  (`[Theory]`), `Validate_EmptyAuthorDisplayName_IsRejected` (`[Theory]`),
  `MaxRawInputLength_MatchesTheDomainContentLimit`.
- `RequestStockQuoteHandlerTests` (9): `Handle_ValidCommand_PublishesTheRequestAndSucceeds`,
  `Handle_ValidCommand_PublishesTheParsedStockCode` (`[Theory]`),
  `Handle_ValidCommand_CarriesTheRoomAndTheRequesterIdentity`,
  `Handle_ValidCommand_StampsTheRequestWithTheInjectedClock`, `Handle_TwoCommands_GetDistinctRequestIds`,
  `Handle_Always_ForwardsTheCancellationToken`, `Handle_NullCommand_Throws`,
  `Constructor_TakesNoPersistenceDependency_SoAStockCommandCanNeverBeWritten` (reflection over the
  constructor — the structural guarantee becomes something the build enforces).
- `PostMessageRegistrationTests` (5): `AddApplication_RegistersTheInternalCommandHandlers`,
  `AddApplication_RegistersTheInternalValidator`, `Send_StockCommand_ReachesTheBrokerAndWritesNothing`
  (real composition, real pipeline, real nested dispatch — no broker, no database),
  `Send_PlainMessage_PersistsAndNeverTouchesTheBroker`,
  `Send_EmptyInput_IsRejectedByThePipelineAsAFailedResult`.

### [x] 1.10 Wire the MassTransit publishers and receive endpoints
Files: `src/Chat.Infrastructure/Messaging/{MassTransitStockQuoteRequester,MassTransitStockQuoteResponder,StockQuoteEndpointExtensions}.cs`, `src/Chat.Infrastructure/DependencyInjection.cs`, `src/Chat.Infrastructure/HealthChecks/*`, `src/Chat.Application/Contracts/Messaging/StockQuoteResolved.cs`, `docs/ARCHITECTURE.md`
Acceptance:
- `IStockQuoteRequester` / `IStockQuoteResponder` implemented over `IPublishEndpoint` and registered by
  `AddMessaging`, so Application still depends on its own abstractions and never on MassTransit
  (`AbstractionsTests.Application_ReferencesNoInfrastructureFramework` still passes).
- Receive endpoints named from `MessagingConstants` (`stock-quote-requests`, `stock-quote-responses`);
  each host passes only its own consumers into `AddMessaging(...)`.
- Retry policy `Interval(RetryLimit, RetryIntervalSeconds)`; exhausted messages land in `<queue>_error`,
  never requeue forever.
- Credentials come from user-secrets/env, never `appsettings.json`.
Decisions taken here (later tasks must conform):
- **Both adapters are `internal sealed` and registered `TryAddScoped`, matching MassTransit's own
  lifetime for `IPublishEndpoint`.** Inside a consumer the scoped endpoint carries the current
  `ConsumeContext`, which is what propagates `ConversationId` across the request→response round trip; a
  singleton adapter would capture a scoped dependency and fail scope validation anyway. They `Publish`
  rather than `Send`: the producer names a message type, not a destination.
- **`StockQuoteEndpointExtensions.AddStockQuoteRequestConsumer<T>()` /
  `AddStockQuoteResponseConsumer<T>()` are the only supported way to register a consumer.** They apply
  the endpoint name from `MessagingConstants` in one place, so 1.15/1.16 cannot invent a queue name and
  cannot silently drift: `ConfigureEndpoints` would otherwise derive one from the class name
  (`StockQuoteRequestConsumer` → `stock-quote-request`, singular), which is neither the documented
  topology nor stable across a rename. Both tasks must call these instead of `AddConsumer<T>()`.
- **`StockQuoteOutcome` now carries `[JsonConverter(typeof(JsonStringEnumConverter<StockQuoteOutcome>))]`.**
  Measured: MassTransit's default `System.Text.Json` options serialise an enum as its ordinal
  (`"outcome":1`), so the "serialised as a string" note written in 0.6 was false and inserting a member
  would have re-interpreted messages already queued. The converter sits on the type, not on a bus
  configuration, so it holds wherever the contract is serialised.
- **The broker health-check question from 0.9 is closed: `RabbitMqHealthCheck` + `AddChatBroker` stay.**
  Re-measured against Chat.Bot running a real receive endpoint on `stock-quote-requests` (a throwaway
  consumer used for the measurement only, never committed): with everything up `masstransit-bus` is
  `Healthy`; with the broker already down at startup it is `Unhealthy` ("Not ready: not started", 503);
  but with the broker stopped **after** a healthy start it reports `Degraded` ("Degraded Endpoints:
  stock-quote-requests") in 4 samples over 60 s — and `Degraded` maps to HTTP 200, so with the bus check
  alone `/health/ready` would answer 200 while no quote can flow. Our probe went `Unhealthy` immediately and drove
  503. The third row is the outage that actually happens, so ours is kept. Table in `ARCHITECTURE.md` §9.
- **Retry and dead-lettering verified against the real broker,** same throwaway consumer, made to throw:
  4 delivery attempts ~2 s apart (initial + `RetryLimit = 3` at `RetryIntervalSeconds = 2`), after which
  the message sat in `stock-quote-requests_error` (1 message) and `stock-quote-requests` was back to 0 —
  no infinite requeue. Both measurement queues and their exchanges were deleted afterwards.
- **Known defect, owned by 1.15, found while measuring:** `dotnet run --project src/Chat.Bot` throws at
  startup. Reproduced at `4f3bd2f` before this task, so it landed with 1.9: `AddApplication()` scans the
  whole assembly, so MediatR registers `PostMessageHandler` and `GetLatestMessagesHandler` in the bot,
  and Development's `ValidateOnBuild` rejects them because the bot has no `IChatRoomRepository`. This
  task removes one of the three failures (`IStockQuoteRequester` now resolves); the bot still needs
  `AddSystemClock()` for `IDateTimeProvider` and a way not to register Web-only handlers.
Unit tests (`tests/Chat.UnitTests/Infrastructure/Messaging/`, 31 cases — no broker, verified green with
the container stopped):
- `StockQuoteContractSerializationTests` (9), against the exact `JsonSerializerOptions` MassTransit puts
  on the wire: `Serializer_RoundTrip_PreservesContract` (`[Theory]` over both contracts),
  `Serializer_RoundTrip_PreservesTheOutcome` (`[Theory]` over all three enum members),
  `Serializer_Outcome_IsWrittenAsItsNameNotItsOrdinal`, `Serializer_RoundTrip_KeepsAnAbsentPriceAbsent`,
  `Serializer_RoundTrip_PreservesThePriceExactly`, `Serializer_RoundTrip_PreservesTheRequestInstant`.
- `StockQuotePublisherTests` (9), over MassTransit's in-memory `ITestHarness`:
  `Publish_StockQuoteRequested_IsSentToBus`, `Publish_StockQuoteResolved_IsSentToBus`,
  `Publish_StockQuoteRequested_CarriesTheContractUnchanged`,
  `Publish_StockQuoteResolved_CarriesTheContractUnchanged`,
  `Publish_StockQuoteRequested_IsPublishedNotSentToAQueue`,
  `RequestAsync_Always_ForwardsTheCallersCancellationToken`,
  `RespondAsync_Always_ForwardsTheCallersCancellationToken`, `RequestAsync_NullRequest_Throws`,
  `RespondAsync_NullResponse_Throws`.
- `StockQuoteEndpointExtensionsTests` (6): `AddStockQuoteRequestConsumer_BindsTheConsumerToTheRequestEndpoint`
  and `AddStockQuoteResponseConsumer_BindsTheConsumerToTheResponseEndpoint` (assert the *received*
  message's input-queue address, not the registration call),
  `EndpointNames_ComeFromMessagingConstants_NotFromTheConsumerClassName` (`[Theory]`),
  `AddStockQuoteRequestConsumer_NullConfigurator_Throws`, `AddStockQuoteResponseConsumer_NullConfigurator_Throws`.
- `AddMessagingTests` (7): `AddMessaging_ResolvesTheOutboundStockQuotePorts` (`[Theory]`),
  `AddMessaging_RegistersTheOutboundPortsAsScoped` (`[Theory]`),
  `AddMessaging_BindsTheBrokerSettingsFromConfiguration`, `RabbitMqOptions_CarryNoDefaultCredentials`,
  `AddMessaging_WithoutConsumers_StillRegistersTheBus`.

### [x] 1.11 Add Identity, authentication and the seeded default room to Chat.Web
Files: `src/Chat.Web/{Program.cs,Identity/IdentityServiceCollectionExtensions.cs,Areas/Identity/Pages/Account/Register.cshtml(.cs),Pages/Chat.cshtml(.cs),Pages/Shared/_LoginPartial.cshtml}`, `src/Chat.Infrastructure/Identity/{ChatClaimTypes,DisplayNameClaimsPrincipalFactory}.cs`, `src/Chat.Infrastructure/Persistence/{ChatDbSeeder,ChatDatabaseInitializationExtensions}.cs`
Acceptance: all met — see the verification below.
Decisions taken here (later tasks must conform):
- **The Register page is scaffolded to capture `DisplayName`** (required, `nvarchar(256)`). The stock
  Identity UI cannot, and an empty name would make every post render blank in the two-browser review.
- **`DisplayNameClaimsPrincipalFactory` puts the name in the auth cookie** as `ChatClaimTypes.DisplayName`
  (`"display_name"`). The hub reads the author from `Context.User`, so this costs no `AspNetUsers` query
  per message. **1.12 must read the display name from this claim, never from a client payload.**
- **Cookie**: `HttpOnly` always, `SameSite=Lax` always, `SecurePolicy=Always` outside Development and
  `SameAsRequest` in Development — the documented local run is plain HTTP on 5271, where an `Always`
  cookie would never come back and every login would silently appear to fail.
- Password policy and lockout are untouched. Only `SignIn.RequireConfirmedAccount = false` is pinned:
  the deliverable ships no email sender, so a required confirmation could never be delivered.
- **`ChatDbSeeder` lives in Infrastructure next to `ChatDbContext`**, so `Chat.Bot` — which never calls
  `AddPersistence()` — cannot reach it. The room is created through `ChatRoom.Create` and the injected
  clock, never raw SQL: seeded data obeys the same invariants as user data.
- **`InitialCreate` was regenerated as a single migration.** Registering Identity in DI makes
  `IdentityDbContext` apply `MaxLengthForKeys = 128`, which diffs against 1.7's 450 on columns that are
  **primary keys** — so EF's generated `ALTER` could never apply (`SqlException 5074: The object
  'PK_AspNetUserTokens' is dependent on column 'Name'`). One coherent migration beats a second one that
  cannot run. Nothing was deployed, so this is safe.
- `ChatHub` is still **not mapped**: a hub with no methods has nothing to authorise, so mapping and
  `[Authorize]` land together in 1.12 with the first method.
- `Pages/Chat.cshtml` is a deliberately minimal authenticated landing point proving the redirect; 1.13
  builds the real page.
Unit tests (`tests/Chat.UnitTests/Infrastructure/Persistence/ChatDbSeederTests.cs`, 7 cases): the seeder
creates the room through the domain factory, stamps it from the injected clock, is idempotent across
repeated calls and fresh boots, **survives losing the insert race to a concurrent instance** (the unique
index is the backstop), and still rethrows any other write failure. Run against SQLite in process memory
so a real unique index is enforced with no container — which required restoring the `SQLitePCLRaw` 2.1.12
pins, since EF's SQLite provider resolves an advisory-carrying 2.1.11.
**Verified** against the running stack:
- Registration through the real UI → `302 -> /Chat`; `AspNetUsers` holds `DisplayName=[Alice Anderson]`
  (len 14), so the display name is genuinely captured and persisted.
- `ChatRooms` holds exactly one `General` row; after a full host restart it is still exactly one, and the
  log reads "schema is up to date; no migration applied".
- Anonymous `GET /Chat` → `302 -> /Identity/Account/Login?ReturnUrl=%2FChat`.
- `FKS_ON_MESSAGES=0` — 1.7's no-foreign-key decision survived the migration regeneration.
- Both hosts start with zero unhandled exceptions; `/health` 200 on each.

### [x] 1.12 Implement ChatHub and the SignalR notifier
Files: `src/Chat.Web/Hubs/ChatHub.cs`, `src/Chat.Web/Program.cs`, `src/Chat.Web/Chat.Web.csproj`, `tests/Chat.UnitTests/{Chat.UnitTests.csproj,Web/*}`
Acceptance: all met — see the verification below. `SignalRChatNotifier` already shipped in 1.10a and was
left untouched; this task only added the tests that pin its group scoping.
Decisions taken here (later tasks must conform):
- **`JoinRoom` returns the history, and joins the group before reading it.** One round trip instead of a
  page fetch plus a subscribe, and the order closes a real gap: reading first would drop any post
  committed between the read and the subscription. In this order the worst case is a post that arrives
  both live and in the history, so **1.13 must de-duplicate by `MessageDto.Id`** — a duplicate is
  recoverable, a missing message is not.
- **The count is not on the wire.** `JoinRoom(Guid)` builds `GetLatestMessagesQuery` with its own default,
  which 1.8 both defaults and caps at 50, so no client can widen the read.
- **`OnDisconnectedAsync` is deliberately not overridden** (a documented deviation from this task's
  original wording). SignalR removes a closed connection from every group it joined, the hub keeps no
  per-connection state to clean up, and the framework already logs connection lifetime at `Debug` — an
  override would be ceremony asserting a cleanup that does not exist. `Hub_KeepsNoMutableState_SoADisconnectHasNothingToCleanUp`
  pins both halves: the only field is the injected `ISender`, and `OnDisconnectedAsync` is still `Hub`'s.
- **Group membership is not restored on reconnect and no server-side map exists to restore it** (that is
  exactly the unbounded per-connection state the resource budget rules out). The client re-joins from its
  `onreconnected` callback; because `JoinRoom` also returns the history, the same call fills whatever the
  connection missed while it was down. **1.13 must wire that callback.**
- **One error channel: `ChatHub.ReceiveError` to `Clients.Caller`.** Only curated `Error.Message` text is
  sent — 1.9 already guarantees untrusted input is never put into an error — and an unexpected exception
  becomes SignalR's own generic message instead, so nothing internal leaks. An error is never sent to the
  group. `JoinRoom` answers a rejected request with an empty history after reporting the error.
- **An unusable room id is rejected at the hub**, before any dispatch: `ChatHub.Errors.InvalidChatRoomId`
  (`ChatRoom.Invalid`). Owned by the transport layer because this is the layer that turns a wire `Guid`
  into a `ChatRoomId`; it also spares the pipeline a dispatch that the validator would only reject.
- **Nothing is returned to the caller on success** (1.9's decision, now enforced by a test over both
  outcomes): a post has already reached the sender through the group broadcast, and a quote request has
  no answer yet.
- **Identity**: `Context.UserIdentifier` for the id and the `display_name` claim for the name, falling
  back to the user name only for a ticket issued before 1.11's claims factory. Empty values are left to
  `PostMessageValidator`, so the hub never has to decide what a missing identity means.
- `ChatHub.Route` (`/hubs/chat`) is a constant so 1.13's script and 1.17's tests cannot drift from the
  mapping. `MapHub` now runs in `Program.cs`, after `UseAuthentication`/`UseAuthorization`.
- `Chat.UnitTests` gained a project reference to `Chat.Web` and `Chat.Web` an `InternalsVisibleTo` for it:
  the hub and the notifier are host code that is worth unit-testing with substituted SignalR abstractions,
  with no server and no connection.
Unit tests (`tests/Chat.UnitTests/Web/`, 24 cases):
- `ChatHubTests` (20): `SendMessage_UsesClaimsIdentity_NotClientPayload` (the substituted
  `HubCallerContext` derives `UserIdentifier` from the subject claim, as `DefaultUserIdProvider` does, so
  the test really reads the ticket), `SendMessage_Signature_AcceptsOnlyTheRoomAndTheText`,
  `SendMessage_TicketWithoutADisplayNameClaim_FallsBackToTheUserName`, `Hub_RequiresAnAuthenticatedCaller`,
  `SendMessage_SuccessfulOutcome_SendsNothingBackToTheCaller` (`[Theory]` over both outcomes),
  `SendMessage_FailedResult_SendsTheErrorToTheCallerOnly`, `SendMessage_FailedResult_NeverReachesTheRoom`,
  `SendMessage_EmptyRoomId_IsRejectedWithoutDispatchingACommand`,
  `SendMessage_Always_ForwardsTheConnectionAbortedToken`,
  `JoinRoom_ValidRoom_AddsTheConnectionToTheRoomGroup`, `JoinRoom_ValidRoom_ReturnsTheHistoryUntouched`,
  `JoinRoom_Always_JoinsTheGroupBeforeReadingTheHistory`,
  `JoinRoom_Always_RequestsTheChallengeLimitAndNoMore`, `JoinRoom_Signature_AcceptsOnlyTheRoom`,
  `JoinRoom_UnknownRoom_ReportsToTheCallerAndReturnsNoHistory`,
  `JoinRoom_EmptyRoomId_IsRejectedWithoutJoiningAGroupOrDispatchingAQuery`,
  `JoinRoom_Always_ForwardsTheConnectionAbortedToken`, `GroupFor_TwoRooms_ProduceDistinctGroupNames`,
  `Hub_KeepsNoMutableState_SoADisconnectHasNothingToCleanUp`.
- `SignalRChatNotifierTests` (4): `BroadcastMessageAsync_Always_SendsToTheRoomGroup`,
  `BroadcastMessageAsync_Always_NeverReachesEveryConnection` (asserts `Clients.All` is never even read),
  `BroadcastMessageAsync_Always_ForwardsTheCancellationToken`, `BroadcastMessageAsync_NullMessage_Throws`.
**Verified** against the running stack (both hosts up, `/health` 200 on each, no unhandled exceptions):
- Anonymous `POST /hubs/chat/negotiate?negotiateVersion=1` → **401** with
  `Location: /Identity/Account/Login?ReturnUrl=%2Fhubs%2Fchat%2Fnegotiate...`; the same request with the
  cookie from a real login → 200 with a connection token.
- Driven over long polling as `alice@example.com`: `JoinRoom` returned `[]` for the empty seeded room;
  a plain line came back as one `ReceiveMessage` frame with `authorDisplayName: "Alice Anderson"` (from
  the claim) and `origin: 1`; `/stock=aapl.us` produced **no** `ReceiveMessage` and no error;
  `/help` produced a caller-only `ReceiveError` with the fixed text and no echo of "help"; an empty room
  id produced `ReceiveError` "A chat room must be selected…" and an empty history.
- `SELECT COUNT(*) FROM Messages WHERE Content LIKE '/%'` → **0** after all of it: neither the stock
  command nor the unknown command reached the table.

### [x] 1.13 Add the minimal chat page
Files: `src/Chat.Web/Pages/Chat.cshtml(.cs)`, `src/Chat.Web/wwwroot/js/chat.js`, `src/Chat.Web/libman.json`,
`src/Chat.Web/wwwroot/lib/signalr/*`, `.config/dotnet-tools.json`,
`src/Chat.Domain/ChatRooms/ChatRoomConstants.cs`, `src/Chat.Application/Contracts/Rooms/ChatRoomDto.cs`,
`src/Chat.Application/Features/Rooms/GetDefaultRoom/*`,
`src/Chat.Application/Abstractions/Persistence/IChatRoomRepository.cs`,
`src/Chat.Infrastructure/Persistence/{ChatDbSeeder.cs,Repositories/ChatRoomRepository.cs}`
Acceptance: all met — see the verification below.
Decisions taken here (later tasks must conform):
- **The page gets its room id from a use case, not from the database.** `IChatRoomRepository` only had
  `Add` and `ExistsAsync`, so the room the window opens on was unreachable without either querying
  `ChatDbContext` from a Razor page (breaking the dependency rule) or hard-coding a GUID. Added instead:
  `GetDefaultRoomQuery` → `GetDefaultRoomHandler` (`IWebFeature`, or no host would register it) →
  `IChatRoomRepository.FindByNameAsync(RoomName)` → `ChatRoomDto(Guid Id, string Name)` in
  `Contracts/Rooms/`, for the same reason `MessageDto` is in `Contracts/Messages/`: a port and a query
  result share it, and 2.2's `ListRooms` will be the third consumer. **2.2 must extend this folder
  (`Features/Rooms/`) rather than start a parallel one**, and should reuse `FindByNameAsync` for
  duplicate-name detection instead of adding a second lookup.
- **The query takes no parameter.** Letting the browser name the room it wants would put a
  client-supplied lookup key into a read path for no benefit; choosing between rooms is the bonus, which
  adds a listing query next to this one rather than widening it.
- **The default room name moved to `Chat.Domain/ChatRooms/ChatRoomConstants.DefaultRoomName`** and
  `ChatDbSeeder.DefaultRoomName` now forwards to it. Two sides depend on the literal agreeing — startup
  creates that room, the page looks it up — and a second copy would silently produce an empty window.
- **`FindByNameAsync` returns a projection built the same way as the "last 50" query**: an internal
  `RoomByNameQuery` composed once (so a test can assert its SQL with `ToQueryString()`) selecting into
  `ChatRoomRow`, because EF Core still cannot translate `room.Name.Value`. Measured on the running host:
  `SELECT TOP(1) [c].[Id], [c].[Name] FROM [ChatRooms] AS [c] WHERE [c].[Name] = @name`.
- **The SignalR JavaScript client is vendored, not fetched from a CDN.** `libman.json` (provider
  `unpkg`) pins `@microsoft/signalr@10.0.11` — MIT, zero npm advisories, same major as the server — into
  `wwwroot/lib/signalr/`, and the files are committed so a clean clone works offline and a reviewer
  behind a proxy still gets a working page. `Microsoft.Web.LibraryManager.Cli` 3.0.114 (MIT, Microsoft)
  joined `.config/dotnet-tools.json` with `rollForward: true` (it targets `net8.0`), so `dotnet libman
  restore` is reproducible. `LICENSE.txt` sits beside the vendored files, as it does for jquery.
- **No `innerHTML`, no `Html.Raw`, no server-rendered message text at all.** The history arrives over the
  hub, and `chat.js` builds each row from `createElement` + `textContent` + `createTextNode`. The only
  values Razor writes are the room id, the room name and the display name, all HTML-encoded by default.
- **The page renders nothing on a successful send** (1.12's decision) and de-duplicates by
  `MessageDto.Id`, because `JoinRoom` subscribes before reading history. `onreconnected` re-joins, which
  also re-reads whatever the connection missed.
- The room id in a data attribute is the *only* room the script can name, and the author is never on the
  wire, so the send box cannot choose an author or a room the connection did not join.
- `ChatModel` takes `ISender` and one `OnGetAsync(CancellationToken)` — bound to `RequestAborted` by the
  framework. A missing room is rendered as an explanation, not an exception: the window then opens no
  connection instead of joining something that cannot exist.
Unit tests (19 new cases; the plan said "none (UI)", but everything added server-side is testable):
- `Application/Features/Rooms/GetDefaultRoomHandlerTests` (6): `Handle_SeededRoom_ReturnsItUntouched`,
  `Handle_Always_LooksUpTheNameTheSeederCreates`, `Handle_UnseededDatabase_ReturnsTheSharedNotFoundFailure`,
  `Handle_Always_ForwardsTheCancellationToken`, `Handle_NullQuery_Throws`, `DefaultRoomName_IsAValidRoomName`.
- `Application/Features/Rooms/GetDefaultRoomRegistrationTests` (2): `AddApplication_RegistersTheInternalQueryHandler`,
  `Send_SeededRoom_ReachesTheHandlerThroughThePipeline`.
- `Infrastructure/Persistence/ChatRoomRepositoryTests` (5): `RoomByNameQuery_FiltersInTheDatabase_AndSelectsOnlyWhatIsRendered`
  (SQL Server provider, no connection), plus SQLite round trips
  `FindByNameAsync_ExistingRoom_ReturnsItsIdentifierAndName`, `FindByNameAsync_UnknownName_ReturnsNull`,
  `FindByNameAsync_DifferentlySpacedName_MatchesTheStoredRoom`, `FindByNameAsync_NullName_Throws`.
- `Web/ChatModelTests` (6): `OnGetAsync_SeededRoom_ExposesTheRoomThePageRenders`,
  `OnGetAsync_UnseededDatabase_ExposesNoRoomInsteadOfFailing`,
  `OnGetAsync_Always_ForwardsTheRequestCancellationToken`,
  `DisplayName_SignedInParticipant_ComesFromTheClaimsAndNotTheRequest`,
  `DisplayName_TicketWithoutADisplayNameClaim_FallsBackToTheUserName`, `Page_RequiresAnAuthenticatedVisitor`.
- `AbstractionsTests.Ports_ExposeTheAsynchronousSurfaceTheRulesAreMeantToCover` gained
  `IChatRoomRepository.FindByNameAsync`, which is what opts the new port method into the async and
  cancellation rules. Suite: **379 passing**.
**Verified** against the running stack (both hosts up, `/health` 200 on each, no unhandled exception in
either log — only the pre-existing "Failed to determine the https port" warning of a plain-HTTP dev run):
- `bob@example.com` / "Bob Brown" registered through the real Register page → `302 -> /Chat`.
- Authenticated `GET /Chat` → **200**, carrying
  `<div id="chat" data-room-id="019fcf15-c0ad-75df-979c-dcbd7b8c5317" data-hub-url="/hubs/chat">`,
  `<script src="/lib/signalr/dist/browser/signalr.min.<hash>.js">` and `<script src="/js/chat.<hash>.js">`;
  the HTML mentions no `cdn`/`unpkg`/`jsdelivr` host.
- Two authenticated SignalR clients (one per user, the reviewer's two browsers): Alice's line reached
  Bob's connection and her own, Bob's reached Alice's and his own, each exactly once, same message ids.
- `/stock=aapl.us` from Alice produced **0** broadcast frames on either connection, and
  `SELECT COUNT(*) FROM Messages WHERE Content LIKE '/%'` → **0**.
- A third connection joining afterwards received both posts once, oldest first, with UTC timestamps.
- Cleanup: the two probe messages were deleted (`Messages` is empty again, `ChatRooms` still 1). The
  `bob@example.com` account was left in place deliberately — 1.18's two-browser run needs a second user.

### [x] 1.14 Add the Stooq client and CSV parser
Files: `src/Chat.Infrastructure/Stocks/{StooqClient,StooqCsvParser}.cs`,
`src/Chat.Infrastructure/DependencyInjection.cs` (`AddStockQuotes` was a no-op stub)
Acceptance: all met. `StooqOptions` already existed from 0.8 and was reused unchanged — one options type
serves both the health probe and the quote client.
Decisions taken here (later tasks must conform):
- **The parser is a pure static type and the client only does HTTP.** `StooqCsvParser.Parse(string?)` is
  the whole of the "understanding Stooq" logic, so every answer the service can give — a quote, `N/D`, a
  truncated body, an HTML error page, prose — is covered by fast offline tests instead of a live call.
- **The price is located by header name, never by position.** The column order is dictated by the `f=`
  query parameter, so a positional read would silently start quoting the Low or the Volume if that
  parameter were ever edited. A row whose field count disagrees with its header is `LookupFailed`.
- **`N/D` in the `Close` field is `SymbolNotFound`; a non-numeric or non-positive close is `LookupFailed`.**
  A zero or negative price is not something the room can act on — "$0.00 per share" is noise dressed up
  as data.
- `decimal.TryParse(..., NumberStyles.Float, CultureInfo.InvariantCulture, ...)`. `InvariantGlobalization`
  is deliberately `false` in this solution, so the ambient culture is real: on de-DE a culture-sensitive
  parse rejects `206.55` outright. The test asserts the chosen culture really disagrees first, so it
  cannot pass vacuously.
- **`StooqClient` never throws except for the caller's own cancellation.** A non-success status, a
  transport error, a client timeout, an open circuit, an oversized body and an unusable body all become
  `StockQuoteLookup.LookupFailed`, as `IStockQuoteProvider` requires. The distinction is one filter:
  `exception is OperationCanceledException && cancellationToken.IsCancellationRequested`. An `HttpClient`
  timeout also surfaces as `TaskCanceledException`, but with the caller's token unsignalled — so a
  timeout is answered politely while a genuine cancellation propagates, instead of the bot posting
  "could not look that up" into a room whose request was already abandoned.
- **The catch is deliberately broad.** This is the boundary that converts a third party's failure modes
  into the vocabulary the bot speaks; catching only `HttpRequestException` would let Polly's
  `BrokenCircuitException` escape and break the port's contract (and would force a Polly reference here).
  Logged at `Warning`: one line per failed `/stock=` command, not per chat message.
- **The URL is built from `StooqOptions.BaseAddress` + `QuotePath` inside the client**, not from
  `HttpClient.BaseAddress`, so the endpoint has exactly one source — the same way `StooqHealthCheck`
  already probes `_options.BaseAddress` explicitly. `Uri.EscapeDataString` is applied to the code even
  though `StockCode`'s allow-list makes it a no-op today, so widening the allow-list cannot turn this
  line into an injection point. The client tests give the stubbed `HttpClient` no base address, so a URL
  built anywhere else would not even be absolute.
- **Measured, and it changed the design: `AddStandardResilienceHandler()` sets `HttpClient.Timeout` to
  `Timeout.InfiniteTimeSpan`.** It appends its own client action, which overrides any earlier one
  (probed: plain client `00:00:07`, same client with the handler `-00:00:00.001`, and re-applying
  `ConfigureHttpClient` afterwards wins back `00:00:07`). So `StooqOptions.TimeoutSeconds` is applied as
  the pipeline's `TotalRequestTimeout` — the framework's intent — rather than being forced back onto the
  client, where it would only add a race able to abort a retry mid-flight. `AttemptTimeout` is that
  budget divided by `StooqClient.MaxAttemptsPerLookup` (3), `Retry.MaxRetryAttempts` is 2 with a 250 ms
  exponential base, and `CircuitBreaker.SamplingDuration` is raised if a generous configured timeout
  would otherwise trip the handler's own `SamplingDuration >= 2 × AttemptTimeout` validation at startup.
  The resilience options are named `StooqClient-standard` (client name + `-standard`, also measured).
- `MaxResponseContentBufferSize = 64 KiB` bounds the buffered body: a quote row is ~70 bytes, and a
  redirected or hostile endpoint must not be able to stream unbounded content into the bot's memory.
- **Registered as a typed *and* named client** (`StooqClient.HttpClientName`), so `IHttpClientFactory`
  owns one pooled handler per process and the registration, the resilience options and the tests all
  name the same string. No collision with 0.8's `AddHttpClient<StooqHealthCheck>` — different names,
  different clients, and the probe keeps its own plain 10 s timeout.
- **Reality check for 1.15/1.18: the challenge's quote endpoint currently answers 404.** Measured
  2026-08-05 from this machine: `GET https://stooq.com/q/l/?s=aapl.us&f=sd2t2ohlcv&h&e=csv` → **404**,
  `Content-Type: text/html`, a 271-byte "The page you requested does not exist" page; identical for
  `&e=csv` without `f=`, for `^spx` and `usdpln`, with and without a browser `User-Agent`, while
  `https://stooq.com/` itself answers 200 (which is why `/health` still reports Stooq healthy) and
  `/q/d/l/` answers with a JavaScript proof-of-work challenge. No live CSV sample could therefore be
  captured; the documented format from the challenge PDF is what the parser and its tests pin. **Until
  it recovers, the end-to-end demo will exercise the friendly `LookupFailed` path** — 1.15 must make
  that wording presentable, and 1.18 must record which path it observed.
Unit tests (`tests/Chat.UnitTests/Infrastructure/Stocks/`, 47 cases; suite **426 passing**, all offline):
- `StooqCsvParserTests` (22): `Parse_ValidRow_ReturnsClosePrice`,
  `Parse_ValidRow_ReadsThePriceFromTheCloseColumnAndNotTheNeighbouringOnes`,
  `Parse_ReorderedColumns_StillReadsTheClosePrice`, `Parse_NotAvailableRow_ReturnsSymbolNotFound`,
  `Parse_HeaderOnly_ReturnsLookupFailed`, `Parse_NoBody_ReturnsLookupFailed` (`[Theory]`, includes null),
  `Parse_MalformedRow_ReturnsLookupFailed` (`[Theory]`, 8 cases including the real HTML error page),
  `Parse_NonPositivePrice_ReturnsLookupFailed` (`[Theory]`),
  `Parse_CommaDecimalCulture_StillParsesInvariant`, `PriceColumn_IsTheCloseColumnOfTheDocumentedHeader`.
- `StooqClientTests` (17, stubbed `HttpMessageHandler`, host `quotes.invalid` so a bypassed stub fails
  loudly instead of calling the real service): `GetQuoteAsync_ValidCsv_ReturnsTheClosingPrice`,
  `GetQuoteAsync_Always_BuildsTheUrlFromTheOptionsAndTheValidatedCode`,
  `GetQuoteAsync_UnknownSymbol_ReturnsSymbolNotFound`,
  `GetQuoteAsync_NonSuccessStatus_ReturnsLookupFailed` (`[Theory]` 404/429/500/503),
  `GetQuoteAsync_HtmlErrorPage_ReturnsLookupFailed`, `GetQuoteAsync_EmptyBody_ReturnsLookupFailed`,
  `GetQuoteAsync_TransportFailure_ReturnsLookupFailed`, `GetQuoteAsync_Timeout_ReturnsLookupFailed`,
  `GetQuoteAsync_UnexpectedTransportException_ReturnsLookupFailed` (`[Theory]`),
  `GetQuoteAsync_CallerCancels_PropagatesTheCancellation`,
  `GetQuoteAsync_Always_LinksTheCallersTokenToTheTransportCall`, `GetQuoteAsync_NullStockCode_Throws`.
- `AddStockQuotesTests` (8): `AddStockQuotes_ResolvesTheQuoteProviderPort`,
  `AddStockQuotes_BindsTheStooqSettingsFromConfiguration`,
  `AddStockQuotes_ConfiguresTheTypedClientFromOptions`,
  `AddStockQuotes_AppliesTheConfiguredTimeoutAsTheResilienceBudget`,
  `AddStockQuotes_TransientFailure_IsRetriedWithinOneLookup` (a 500 really is retried to
  `MaxAttemptsPerLookup` through the registered pipeline, then answered as `LookupFailed`),
  `AddStockQuotes_SuccessfulLookup_IssuesExactlyOneRequest`,
  `StooqOptions_Defaults_MatchTheChallengeEndpoint`, `AddStockQuotes_NullConfiguration_Throws`.
**Verified:** `dotnet run --project src/Chat.Bot` still starts clean with the typed client registered —
`/health/live` 200, `/health` 200 with `masstransit-bus`, `rabbitmq` and `stooq` all healthy, no
unhandled exception. The bot still calls no `AddPersistence()`.

### [x] 1.14a Tell the participant when the quote service is down
Files: `src/Chat.Application/Contracts/Realtime/ChatAlert.cs`, `src/Chat.Application/Abstractions/Realtime/IChatNotifier.cs`, `src/Chat.Web/Realtime/SignalRChatNotifier.cs`, `src/Chat.Web/Hubs/ChatHub.cs`, `src/Chat.Web/Pages/Chat.cshtml`, `src/Chat.Web/wwwroot/js/chat.js`

1.14 measured that Stooq's `/q/l/` CSV endpoint now answers **404 with an HTML page**, so a reviewer's
`/stock=` will take the `LookupFailed` path. A chat line saying so would scroll away among the messages;
a participant needs to know the *system* is degraded and that retrying shortly is worth it.
- `ChatAlert(Message, Severity)` with `ChatAlert.QuoteServiceUnavailable` — the wording lives in one
  place so the page, the tests and 1.16 cannot disagree about it.
- `IChatNotifier.NotifyAlertAsync(userId, alert, ct)` delivers it to **one participant's** connections via
  `Clients.User(userId)` — not the room (somebody else's failed lookup is noise) and not `Clients.All`.
  An alert is never persisted and never appears in the last-50 history, because it is not a post.
- `ChatHub.ReceiveAlert` is the client method; `chat.js` renders a dismissible red banner
  (`alert-danger` for `Severity.Error`, amber otherwise) with `textContent`, outside the message list.
- The trigger is 1.16's to supply — see the "outage banner" block in that entry.
Unit tests (`tests/Chat.UnitTests/Web/SignalRChatNotifierTests.cs`, 8 new): the alert reaches only that
participant's connections, reaches neither the room nor every connection, forwards the cancellation
token, rejects a missing recipient or alert, and `QuoteServiceUnavailable` really is an error-severity
message naming Stooq and telling the participant to try again.

### [x] 1.15 Implement the bot use case and worker
Files: `src/Chat.Application/Features/StockCommands/ResolveStockQuote/{ResolveStockQuoteCommand,ResolveStockQuoteHandler,StockQuoteAnswer}.cs`,
`src/Chat.Application/Contracts/Messaging/StockQuoteResolved.cs`, `src/Chat.Bot/{StockQuoteRequestConsumer.cs,Program.cs,Chat.Bot.csproj}`,
`tests/Chat.UnitTests/Chat.UnitTests.csproj`
Acceptance: all met — see the verification below.
Decisions taken here (later tasks must conform):
- **There is no `BackgroundService`, and this task's original wording is corrected rather than obeyed.**
  That line predates task 0.9's move from raw `RabbitMQ.Client` to MassTransit: the bus is already a
  hosted service that owns the connection, the prefetch window and the retry policy, and it *pushes*
  `StockQuoteRequested` into an `IConsumer<T>`. A hand-rolled polling loop beside it would duplicate all
  of that and lose what 1.10 measured — 4 attempts ~2 s apart, then `stock-quote-requests_error` instead
  of an infinite requeue. The requirement the wording protected (fully async, no blocking call, honours
  the stopping token) is met by taking `ConsumeContext.CancellationToken` as the only token the use case
  ever sees; MassTransit cancels it on shutdown. Recorded on the consumer type itself.
- **`StockQuoteResolved` now carries `RequestedByUserId`** (additive, mirroring `StockQuoteRequested`'s
  member order). Added here rather than in 1.16 because the **bot is the publisher** and it already
  receives the value; 1.16 only reads it. Round-trip test added:
  `Serializer_RoundTrip_PreservesTheRequesterTheAlertIsAimedAt`.
- **The wording lives in `StockQuoteAnswer`, three pure static methods, not inline in the handler.** It is
  the bot's only user-visible output and the one string the challenge quotes verbatim, so it is pinned by
  offline tests. The three lines are:
  - `Quoted`: `"AAPL.US quote is $93.42 per share"` — `Display` for the ticker, `price.ToString("F2",
    CultureInfo.InvariantCulture)`, then interpolated as a string so no ambient culture can reach the
    output through either the number or the string-building. `InvariantGlobalization` is deliberately
    `false` in this solution, so a de-DE machine would otherwise post `"$93,42"`.
  - `SymbolNotFound`: `"Sorry, I could not find a quote for AAPL.XX."` — the wording `ARCHITECTURE.md` §5
    already documented, kept rather than reinvented.
  - `Unavailable` (`LookupFailed`): `"I could not reach the quote service, so I have no price for AAPL.US
    right now."` This is the line a reviewer actually reads, because Stooq's `/q/l/` endpoint has answered
    404 since 1.14. It names the ticker (several lookups can be in flight), blames the upstream rather
    than the bot, and deliberately carries **no** "try again in a couple of minutes" — 1.14a's
    `ChatAlert.QuoteServiceUnavailable` delivers that instruction as a banner, so the two complement each
    other instead of repeating.
- **The handler answers on every path and returns `Result.Success()` for all three outcomes.** An unknown
  ticker or an unreachable provider is an answer, not a failed use case: a failed `Result` would only make
  the consumer log and the room stay silent.
- **The one `catch` is a contract backstop, not the outage handling.** `IStockQuoteProvider` already
  converts every Stooq failure mode into `LookupFailed` (1.14), so this `catch` exists for a provider that
  breaks that promise — logged at **Error**, because it means our own code is wrong, unlike the client's
  Warning for "Stooq is having a bad day". Genuine caller cancellation is excluded by the same
  token-signalled filter `StooqClient` uses and propagates, so an abandoned delivery does not post noise
  into a room nobody is waiting on.
- **`RespondAsync` is deliberately outside that backstop.** A refused publish is not an answer the bot can
  reword, so it propagates and MassTransit retries then dead-letters. Swallowing it would lose the request
  silently.
- **A `Quoted` outcome with a null price is downgraded to `LookupFailed`** before anything is published, so
  the outcome 1.16 reads to decide about the alert always agrees with the text the participant sees.
  `StockQuoteLookup.Quoted(decimal)` cannot produce that, but the record's primary constructor can — and
  `"$0.00 per share"` is noise dressed up as data, the same call 1.14's parser made about a zero close.
- **`ResolveStockQuoteCommand` carries a `StockCode`, not a string**, exactly like `RequestStockQuoteCommand`,
  so mapping the wire string is the consumer's job at the boundary and an unvalidated ticker cannot reach
  the Stooq URL. **No validator**: the only field with a rule is the value object, and a defaulted room id
  is already an expected failure in 1.16 (`ChatRoomErrors.NotFound`).
- **One failure rule in the consumer:** an expected failure (a ticker `StockCode.Create` rejects, a failed
  `Result` from the dispatch) is logged and acknowledged, because an identical redelivery would reproduce
  it four times and then dead-letter something already understood; an unexpected exception propagates so
  MassTransit applies its retry policy and the `_error` queue. No manual nack path exists.
- The handler is marked `IBotFeature`, takes no persistence port, and logs one Information line per
  resolved command (per `/stock=`, not per chat message).
- `Chat.Bot` gained `InternalsVisibleTo` for the test projects and `Chat.UnitTests` a project reference to
  it, for the same reason 1.12 did that for `Chat.Web`: the inbound adapter is host code worth unit-testing
  with a substituted `ConsumeContext` and MassTransit's in-memory `ITestHarness`, with no broker.
Unit tests (33 new cases; suite **467 passing**, all offline):
- `Application/Features/StockCommands/ResolveStockQuoteHandlerTests` (17):
  `Handle_ValidQuote_PublishesExpectedMessageFormat` (the exact string),
  `Handle_ValidQuote_RendersTwoDecimalsAndTheUpperCaseTicker` (`[Theory]`, 4 cases),
  `Handle_CommaDecimalCulture_StillFormatsThePriceWithADot` (asserts de-DE really disagrees first, so it
  cannot pass vacuously), `Handle_ValidQuote_PublishesTheOutcomeAndThePriceAlongsideTheText`,
  `Handle_SymbolNotFound_PublishesFriendlyMessage`, `Handle_LookupFailed_PublishesAFriendlyOutageMessage`,
  `Handle_ProviderThrows_PublishesLookupFailedAndDoesNotRethrow`,
  `Handle_QuotedWithoutAPrice_IsDowngradedToLookupFailed`,
  `Handle_Always_EchoesTheCorrelationRoomTickerAndRequester`,
  `Handle_Always_StampsTheAnswerWithTheInjectedClock`,
  `Handle_Always_ForwardsTheCancellationTokenToTheLookupAndThePublish`,
  `Handle_CallerCancels_PropagatesTheCancellationWithoutPublishing`,
  `Handle_PublishFails_PropagatesSoTheDeliveryIsRetried`,
  `Handle_Always_LooksUpTheValidatedCodeExactlyOnce`, `Handle_NullCommand_Throws`,
  `Handler_IsMarkedAsABotFeature_SoChatBotRegistersIt`,
  `Constructor_TakesNoPersistenceDependency_SoTheBotStaysDecoupledFromTheDatabase`.
- `Bot/StockQuoteRequestConsumerTests` (12): `Consume_StockQuoteRequested_DispatchesTheResolveCommand`,
  `Consume_UnnormalisedStockCode_IsRebuiltThroughTheValueObject` (`[Theory]`),
  `Consume_UnusableStockCode_DispatchesNothingAndDoesNotFault` (`[Theory]`, 5 cases including
  `aapl.us&f=x` and `../../etc/passwd`), `Consume_Always_ForwardsTheConsumeCancellationToken`,
  `Consume_DispatchFails_AcknowledgesInsteadOfFaultingTheDelivery`, `Consume_NullContext_Throws`,
  `Consume_PublishedRequest_ReachesTheConsumerOnTheRequestQueue` (in-memory `ITestHarness`: asserts the
  input-queue address, that the delivery did not fault, and that the use case was dispatched).
- `StockQuoteContractSerializationTests` gained `Serializer_RoundTrip_PreservesTheRequesterTheAlertIsAimedAt`.
**Verified** against the running stack (both hosts up, `/health` 200 on each, no unhandled exception):
- `Chat.Bot` logs `Configured endpoint stock-quote-requests, Consumer: Chat.Bot.StockQuoteRequestConsumer`;
  the management API reports `stock-quote-requests` `durable=True state=running consumers=1`.
- `/stock=aapl.us` sent from an authenticated SignalR client (`alice@example.com`, long polling, identity
  from the cookie) produced **no** `ReceiveMessage` frame, and in the bot log:
  `Stooq answered 404 for aapl.us; reporting a failed lookup.` then
  `Resolved aapl.us for request 019fd26b-72d6-744d-8cf4-f1abb59262c4 as LookupFailed.` — the
  `LookupFailed` path 1.14 predicted, exercised end to end with no crash and no dead-letter.
- The answer really reached the broker and has nowhere to go until 1.16 exists: exchange
  `Chat.Application.Contracts.Messaging:StockQuoteResolved` reports `publish_in: 1` with **zero bindings**
  and no outgoing route, `stock-quote-responses` returns **404** from the management API, and there is no
  `_skipped` queue — that suffix only exists for a receive endpoint that received a message it had no
  consumer for, which is not this case. RabbitMQ discarded the unroutable message.
- A plain control line posted and broadcast normally in the same session, and
  `SELECT COUNT(*) FROM Messages WHERE Content LIKE '/%'` → **0**. The control row was deleted afterwards
  (`Messages` empty again, `ChatRooms` still 1).

### [ ] 1.16 Consume quote responses in Chat.Web and post them as the bot
Files: `src/Chat.Web/Messaging/StockQuoteResponseConsumer.cs`, `src/Chat.Application/Features/Messages/PostBotMessage/*`
Acceptance:
- Consumer deserialises `StockQuoteResolved` and sends `PostBotMessageCommand`.
- `PostBotMessageHandler` creates a `Message` via `Message.PostByBot`, persists it and broadcasts to the room group.
- Unparseable payloads are dead-lettered, not requeued.
Inherited from 1.10 (do not rediscover):
- Register the consumer with `configurator.AddStockQuoteResponseConsumer<StockQuoteResponseConsumer>()`,
  never `AddConsumer<T>()`, so the endpoint stays `MessagingConstants.StockQuoteResponseQueue`.
- Dead-lettering needs no code: measured against the real broker, `Interval(3, 2s)` runs 4 attempts and
  then MassTransit moves the message to `<queue>_error`. Do not add a manual nack path.
- Reuse `Chat.Application/Errors/ChatRoomErrors.NotFound` (promoted in 1.9) for the unknown-room failure.
Inherited from 1.14a — **the outage banner**:
- When `StockQuoteResolved.Outcome == StockQuoteOutcome.LookupFailed`, also call
  `IChatNotifier.NotifyAlertAsync(response.RequestedByUserId, ChatAlert.QuoteServiceUnavailable, ct)`.
  The plumbing and the client rendering already exist; 1.16 only supplies the trigger.
- `SymbolNotFound` does **not** raise an alert — an unknown ticker is a real answer from a working
  service, so it stays a bot chat message. Only `LookupFailed` means "the provider is down, retry".
Inherited from 1.15 (do not rediscover):
- **`StockQuoteResolved.RequestedByUserId` already exists.** 1.15 added it (the bot is the publisher and
  already had the value) and the round-trip test
  `Serializer_RoundTrip_PreservesTheRequesterTheAlertIsAimedAt` pins it. Read it, do not re-add it.
- **Post `StockQuoteResolved.Message` verbatim.** The bot owns the wording (`StockQuoteAnswer`, three
  lines, unit-tested); re-formatting or appending in Chat.Web would put the challenge's graded sentence in
  two places. `Price` and `Outcome` are for the alert decision and logging, not for rendering.
- The answer is always present for all three outcomes — 1.15 never publishes an empty message and never
  returns a failed `Result` for a lookup outcome — so `Message` needs no fallback text here.
- Today a published `StockQuoteResolved` is **unroutable**: the exchange exists with zero bindings and
  RabbitMQ discards the message (measured in 1.15). Registering this consumer is what creates
  `stock-quote-responses` and its binding, so the first thing to verify is that the queue appears.
Unit tests: `Handle_BotMessage_PersistsWithBotAuthorAndBroadcasts`, `Handle_UnknownRoom_ReturnsFailureWithoutBroadcast`, `Handle_LookupFailed_AlertsTheRequesterOnly`, `Handle_SymbolNotFound_RaisesNoAlert`.

### [ ] 1.17 Add the integration test suite
Files: `tests/Chat.IntegrationTests/{CustomWebApplicationFactory.cs,...}`
Acceptance:
- Factory starts a throwaway SQL Server container via `Testcontainers.MsSql` (same provider as production), applies migrations once per collection, substitutes `IStockQuoteRequester`, and provides a test-auth helper. Overriding `ConnectionStrings:ChatDatabase` with the container's string is enough — `AddPersistence` reads only that key, and throws if it is missing.
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
