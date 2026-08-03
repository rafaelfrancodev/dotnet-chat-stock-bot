---
name: clean-architecture
description: Enforce Clean Architecture layering and dependency rules for this .NET solution. Use whenever creating projects, adding classes, deciding where code belongs, wiring dependency injection, or reviewing structure. Trigger for any mention of "architecture", "layer", "project structure", "where should this go", or when scaffolding new features.
---

# Clean Architecture (.NET)

## Solution layout

```
src/
  Chat.Domain/          # Entities, Value Objects, Domain Events, domain interfaces. NO external deps.
  Chat.Application/     # Use cases: Commands, Queries, Handlers, DTOs, validators, abstractions (interfaces for infra).
  Chat.Infrastructure/  # EF Core, Identity, RabbitMQ, Stooq HTTP client, repository implementations.
  Chat.Web/             # ASP.NET Core host: SignalR hubs, controllers/Razor pages, DI composition root.
  Chat.Bot/             # Decoupled bot worker (separate process): consumes stock commands, calls Stooq, publishes quotes.
tests/
  Chat.UnitTests/
  Chat.IntegrationTests/
```

## Dependency rule (strict)

- Dependencies point INWARD only: Web/Bot -> Infrastructure -> Application -> Domain.
- Domain references nothing (no EF, no ASP.NET).
- Application references Domain only; defines interfaces (IMessageRepository, IStockQuoteRequester, IChatNotifier) implemented by Infrastructure/Web.
- Infrastructure implements Application/Domain interfaces; never referenced by Application.
- The composition root (Program.cs in Web and Bot) is the ONLY place that knows all layers.

## Rules of thumb

1. If a class imports Microsoft.EntityFrameworkCore it belongs in Infrastructure.
2. If a class imports Microsoft.AspNetCore.SignalR it belongs in Web (or an abstraction in Application + adapter in Web).
3. Business rules that don't depend on persistence -> Domain. Orchestration of a use case -> Application handler.
4. Cross-cutting concerns (logging, validation) via pipeline behaviors, not scattered in handlers.
5. The bot is a separate deployable. It shares Domain/Application contracts via project reference or a small Contracts project — never via the Web project.

## Checklist before finishing any task

- [ ] No outward dependency violations (dotnet list reference sanity check).
- [ ] New interfaces live in Application/Domain; implementations in Infrastructure.
- [ ] DI registrations grouped per layer in extension methods (AddApplication(), AddInfrastructure()).
