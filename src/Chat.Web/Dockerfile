# syntax=docker/dockerfile:1
#
# Chat.Web — the SignalR host, Identity UI and Razor pages.
# Built from the repository root, because central package management and the shared build props live
# there:  docker build -f src/Chat.Web/Dockerfile .

# ---- build -------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer: these files change far less often than source, so editing code reuses the
# cached package graph. Directory.Packages.props carries every version (central package management) and
# .editorconfig has to come too — EnforceCodeStyleInBuild turns style rules into build errors, so
# without it the image would compile under different rules from the developer's machine.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Chat.Domain/Chat.Domain.csproj src/Chat.Domain/
COPY src/Chat.Application/Chat.Application.csproj src/Chat.Application/
COPY src/Chat.Infrastructure/Chat.Infrastructure.csproj src/Chat.Infrastructure/
COPY src/Chat.Web/Chat.Web.csproj src/Chat.Web/
RUN dotnet restore src/Chat.Web/Chat.Web.csproj

COPY src/ src/
RUN dotnet publish src/Chat.Web/Chat.Web.csproj -c Release -o /app --no-restore

# ---- runtime -----------------------------------------------------------------------------------
# Debian-based on purpose, never -alpine: the Alpine images ship without ICU and set
# DOTNET_SYSTEM_GLOBALIZATION_INVARIANT, under which Microsoft.Data.SqlClient throws "Globalization
# Invariant Mode is not supported" the moment it opens a connection. Directory.Build.props pins
# InvariantGlobalization=false for the same reason.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Only for the container healthcheck: the runtime image has no HTTP client of its own, and a
# healthcheck is what tells Coolify the app is up rather than merely started.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# Data-protection keys decide whether an authentication cookie survives a redeploy. Without a home
# directory ASP.NET Core keeps them in memory, so every deployment would sign every user out and
# invalidate the antiforgery tokens on any open page. Created and owned before the volume is mounted,
# because Docker seeds a fresh named volume from the image — including ownership.
ENV HOME=/home/app
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R $APP_UID /home/app

USER $APP_UID

# 3100 rather than the image default of 8080, so one number describes this service everywhere: the port
# the app listens on, the port compose publishes, and the port a reverse proxy has to be told about.
# Coolify's "Domains" field takes the *container* port after the colon — with the app on 8080 and the
# domain written as :3100, Traefik forwards to a port nothing is listening on and answers 502.
ENV ASPNETCORE_HTTP_PORTS=3100
EXPOSE 3100

ENTRYPOINT ["dotnet", "Chat.Web.dll"]
