#Requires -Version 7.0
<#
.SYNOPSIS
    Configures both hosts' user-secrets from .env, so local credentials are written once.

.DESCRIPTION
    `.env` is read only by `docker compose`, which uses it to CREATE the containers. The applications
    never read it — they read appsettings.json, user-secrets and environment variables. That means the
    same credentials have to exist in two places, and if they drift the containers accept a password the
    apps do not send: RabbitMQ refuses the connection and SQL Server refuses the login.

    This script removes the copying by hand. `.env` stays the single source of truth for local
    credentials, and the values land in the right store for each host:

      Chat.Web  -> ConnectionStrings:ChatDatabase, RabbitMq:UserName, RabbitMq:Password
      Chat.Bot  -> RabbitMq:UserName, RabbitMq:Password, and Finnhub:ApiKey when you pass one

    Values are never printed. Re-running is safe; it overwrites.

.PARAMETER FinnhubApiKey
    Free key from finnhub.io. Optional: without it the bot answers a friendly failure instead of a price.

.EXAMPLE
    pwsh ./scripts/set-dev-secrets.ps1
.EXAMPLE
    pwsh ./scripts/set-dev-secrets.ps1 -FinnhubApiKey "d1..."
#>
[CmdletBinding()]
param(
    [string]$FinnhubApiKey
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $repoRoot '.env'

if (-not (Test-Path $envPath)) {
    throw ".env not found at $envPath. Copy it first:  Copy-Item .env.example .env"
}

# Parse .env: KEY=VALUE, ignoring blanks and comments. Values may contain '=', so split on the first only.
$settings = @{}
foreach ($line in Get-Content $envPath) {
    $trimmed = $line.Trim()
    if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }

    $separator = $trimmed.IndexOf('=')
    if ($separator -lt 1) { continue }

    $settings[$trimmed.Substring(0, $separator).Trim()] = $trimmed.Substring($separator + 1).Trim()
}

function Get-Required([string]$name) {
    if (-not $settings.ContainsKey($name) -or [string]::IsNullOrWhiteSpace($settings[$name])) {
        throw "$name is missing or empty in .env"
    }

    return $settings[$name]
}

$saPassword = Get-Required 'MSSQL_SA_PASSWORD'
$rabbitUser = Get-Required 'RABBITMQ_USER'
$rabbitPassword = Get-Required 'RABBITMQ_PASSWORD'
$sqlPort = if ($settings.ContainsKey('MSSQL_PORT') -and $settings['MSSQL_PORT']) { $settings['MSSQL_PORT'] } else { '1433' }

# 127.0.0.1 rather than localhost: the containers publish on the IPv4 loopback only, and localhost
# resolves to ::1 first on Windows, which costs a full SqlClient connection timeout before it falls back.
$connectionString = "Server=127.0.0.1,$sqlPort;Database=ChatDb;User Id=sa;Password=$saPassword;Encrypt=True;TrustServerCertificate=True"

$web = Join-Path $repoRoot 'src/Chat.Web'
$bot = Join-Path $repoRoot 'src/Chat.Bot'

function Set-Secret([string]$project, [string]$key, [string]$value) {
    # The value is passed as a single argument, so symbols in a password need no escaping, and it is
    # swallowed rather than echoed: the tool prints "Successfully saved <key> = <value>".
    dotnet user-secrets set $key $value --project $project | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to set $key for $project" }

    Write-Host "  $key" -ForegroundColor DarkGray
}

Write-Host 'Chat.Web' -ForegroundColor Cyan
Set-Secret $web 'ConnectionStrings:ChatDatabase' $connectionString
Set-Secret $web 'RabbitMq:UserName' $rabbitUser
Set-Secret $web 'RabbitMq:Password' $rabbitPassword

Write-Host 'Chat.Bot' -ForegroundColor Cyan
Set-Secret $bot 'RabbitMq:UserName' $rabbitUser
Set-Secret $bot 'RabbitMq:Password' $rabbitPassword

if ($FinnhubApiKey) {
    Set-Secret $bot 'Finnhub:ApiKey' $FinnhubApiKey
}
else {
    Write-Host '  Finnhub:ApiKey not set — the bot will answer a friendly failure instead of a price.' -ForegroundColor Yellow
    Write-Host '  Add one with: pwsh ./scripts/set-dev-secrets.ps1 -FinnhubApiKey "<key>"' -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Done. Values came from .env and were not printed.' -ForegroundColor Green
