#Requires -Version 7.0
<#
.SYNOPSIS
    Runs Chat.Web and Chat.Bot together for local development.

.DESCRIPTION
    Starts both hosts as child processes, waits for their /health/live endpoints, prints the URLs and
    then streams until you press Ctrl+C — which stops both. Use this when you are not in Visual Studio;
    inside VS, pick the "Chat.Web + Chat.Bot" startup profile instead.

    Expects the infrastructure to be running already:
        docker compose -f docker-compose.dev.yml up -d

.PARAMETER SkipBuild
    Skip the up-front build (faster when you have just built).
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$webUrl = 'http://localhost:5271'
$botUrl = 'http://localhost:5299'

function Wait-ForHealth {
    param([string]$Url, [string]$Name, [int]$TimeoutSeconds = 90)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $null = Invoke-WebRequest -Uri "$Url/health/live" -TimeoutSec 5 -UseBasicParsing
            Write-Host "  $Name is live at $Url" -ForegroundColor Green
            return $true
        }
        catch {
            Start-Sleep -Seconds 2
        }
    }

    Write-Host "  $Name did not become live within $TimeoutSeconds s" -ForegroundColor Red
    return $false
}

if (-not $SkipBuild) {
    Write-Host 'Building...' -ForegroundColor Cyan
    dotnet build "$repoRoot/Chat.sln" --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

$processes = @()
try {
    Write-Host 'Starting Chat.Web and Chat.Bot...' -ForegroundColor Cyan

    $processes += Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', "$repoRoot/src/Chat.Web", '--launch-profile', 'http' `
        -NoNewWindow -PassThru

    $processes += Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--project', "$repoRoot/src/Chat.Bot" `
        -NoNewWindow -PassThru

    $webReady = Wait-ForHealth -Url $webUrl -Name 'Chat.Web'
    $botReady = Wait-ForHealth -Url $botUrl -Name 'Chat.Bot'

    if (-not ($webReady -and $botReady)) {
        throw 'One or both hosts failed to start. Check the output above.'
    }

    Write-Host ''
    Write-Host "Chat:            $webUrl" -ForegroundColor Cyan
    Write-Host "Web health:      $webUrl/health" -ForegroundColor Cyan
    Write-Host "Bot health:      $botUrl/health" -ForegroundColor Cyan
    Write-Host "RabbitMQ admin:  http://localhost:15672" -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'Press Ctrl+C to stop both hosts.' -ForegroundColor Yellow

    while ($processes | Where-Object { -not $_.HasExited }) {
        Start-Sleep -Seconds 1
    }
}
finally {
    foreach ($process in $processes) {
        if ($null -ne $process -and -not $process.HasExited) {
            Write-Host "Stopping PID $($process.Id)..." -ForegroundColor DarkGray
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
