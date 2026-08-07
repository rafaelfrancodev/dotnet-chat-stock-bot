# syntax=docker/dockerfile:1
#
# Chat.Bot — the decoupled quote worker. It consumes stock-quote requests from RabbitMQ, calls the
# configured quote provider and publishes the answer back; it serves /health and nothing else.
# Built from the repository root:  docker build -f src/Chat.Bot/Dockerfile .
#
# Note what is absent: no database, no connection string, no persistence dependency of any kind. That
# is the challenge's decoupling requirement, and BotCompositionTests fails the build if it slips.

# ---- build -------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Chat.Domain/Chat.Domain.csproj src/Chat.Domain/
COPY src/Chat.Application/Chat.Application.csproj src/Chat.Application/
COPY src/Chat.Infrastructure/Chat.Infrastructure.csproj src/Chat.Infrastructure/
COPY src/Chat.Bot/Chat.Bot.csproj src/Chat.Bot/
RUN dotnet restore src/Chat.Bot/Chat.Bot.csproj

COPY src/ src/
RUN dotnet publish src/Chat.Bot/Chat.Bot.csproj -c Release -o /app --no-restore

# ---- runtime -----------------------------------------------------------------------------------
# Debian, not -alpine: see src/Chat.Web/Dockerfile. The bot reaches no database, but it shares
# Chat.Infrastructure, so the same globalization requirement applies to it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "Chat.Bot.dll"]
