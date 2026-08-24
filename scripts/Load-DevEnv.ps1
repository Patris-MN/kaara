<#
.SYNOPSIS
Loads local development environment variables from infra/docker/.env.

.DESCRIPTION
The application reads database passwords only from environment variables
(see src/Backend/Host/PTS.Host/Persistence/LocalPostgresConnectionStrings.cs),
so those variables must be present in the shell before `dotnet run` or
`dotnet ef` is invoked. This script is the single place that translates the
git-ignored .env file into that shell state, so the two can never drift.

Dot-source it to affect the current session:

    . .\scripts\Load-DevEnv.ps1
#>
[CmdletBinding()]
param(
    [string] $EnvFile = (Join-Path $PSScriptRoot '..\infra\docker\.env')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $EnvFile)) {
    throw "Environment file '$EnvFile' not found. Copy infra/docker/.env.example to " +
          "infra/docker/.env and set your local passwords."
}

foreach ($line in Get-Content -LiteralPath $EnvFile) {
    $trimmed = $line.Trim()
    if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }

    $separator = $trimmed.IndexOf('=')
    if ($separator -lt 1) { continue }

    $name = $trimmed.Substring(0, $separator).Trim()
    $value = $trimmed.Substring($separator + 1).Trim().Trim('"', "'")

    Set-Item -Path "Env:$name" -Value $value
    Write-Verbose "Set $name"
}

Write-Host "Loaded development environment from $([System.IO.Path]::GetFullPath($EnvFile))"
