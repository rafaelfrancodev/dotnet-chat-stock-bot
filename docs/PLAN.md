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

### [ ] 1.11 Add Identity, authentication and the seeded default room to Chat.Web
Files: `src/Chat.Web/Program.cs`, `src/Chat.Web/Areas/Identity/*`, `src/Chat.Infrastructure/Persistence/ChatDbSeeder.cs`
Acceptance:
- Register/login/logout work through the default Identity UI; cookie is HttpOnly + SameSite=Lax + Secure.
- `ApplicationUser` (1.7) already exists with `DisplayName` (`nvarchar(256)`, required): wire
  `AddDefaultIdentity<ApplicationUser>().AddEntityFrameworkStores<ChatDbContext>()` and capture the
  display name at registration — an empty one would make every post render blank.
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
Inherited from 1.10 (do not rediscover):
- Register the consumer with `configurator.AddStockQuoteRequestConsumer<StockQuoteRequestConsumer>()`,
  never `AddConsumer<T>()` — that extension is what pins the endpoint to
  `MessagingConstants.StockQuoteRequestQueue`. `IStockQuoteResponder` is already registered by
  `AddMessaging`, so the bot only has to consume and publish.
- **Mark `ResolveStockQuoteHandler` with `IBotFeature`** (from 1.10a) or the bot will not register it and
  the request consumer will fail with "no handler for request". It must not take any persistence
  dependency — `HostFeatureTests.BotHost_RegistersNoHandlerThatNeedsPersistenceOrTheChatSurface` enforces
  that. The bot's startup defect itself is already fixed in 1.10a.
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
Inherited from 1.10 (do not rediscover):
- Register the consumer with `configurator.AddStockQuoteResponseConsumer<StockQuoteResponseConsumer>()`,
  never `AddConsumer<T>()`, so the endpoint stays `MessagingConstants.StockQuoteResponseQueue`.
- Dead-lettering needs no code: measured against the real broker, `Interval(3, 2s)` runs 4 attempts and
  then MassTransit moves the message to `<queue>_error`. Do not add a manual nack path.
- Reuse `Chat.Application/Errors/ChatRoomErrors.NotFound` (promoted in 1.9) for the unknown-room failure.
Unit tests: `Handle_BotMessage_PersistsWithBotAuthorAndBroadcasts`, `Handle_UnknownRoom_ReturnsFailureWithoutBroadcast`.

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
