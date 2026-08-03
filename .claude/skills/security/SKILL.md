---
name: security
description: Security requirements for this chat application. Apply whenever touching authentication, Identity, SignalR hubs, configuration/secrets, HTTP clients, message broker, database access, or user input. Also trigger for any code review.
---

# Security

## Authentication & authorization

- ASP.NET Core Identity for registration/login (bonus requirement — do it).
- Every hub and messaging endpoint requires [Authorize]. Never trust a client-supplied username: author identity comes from Context.User / claims, server-side.
- Cookie auth is fine for this browser app; HttpOnly, Secure, SameSite=Lax minimum.
- Keep Identity defaults for password policy and lockout; do not weaken them.

## Input handling

- Treat all chat input as hostile. Validate length and content server-side (Value Objects do this).
- XSS: render messages as text on the client, never via innerHTML/Html.Raw. Encode on output.
- Stock code: validate against a strict pattern (^[a-z0-9.\-]{1,20}$ after normalization) BEFORE building the Stooq URL — prevents URL/parameter injection.
- SQL injection: EF Core parameterized queries only; no raw SQL string concatenation.

## Secrets & configuration

- NO secrets in the repo: connection strings and RabbitMQ credentials via user-secrets (dev) / environment variables. appsettings.json contains placeholders only.
- Verify .gitignore covers appsettings.*.Local.json, .env, *.user.
- Explicit challenge requirement: "Keep confidential information secure" — check before every commit and before zipping the deliverable.

## Transport & infra

- HTTPS redirection on; HSTS outside Development.
- RabbitMQ: credentials from environment; avoid default guest/guest outside local docker-compose.
- Rate-limit message posting per user (middleware or hub-side throttle) to prevent flooding — also a resource concern.

## Review checklist

- [ ] No secret literals (grep -riE "password|secret|apikey" src/ sanity pass)
- [ ] All hubs [Authorize]
- [ ] User identity from claims, never from payload
- [ ] Stock code validated before outbound call
