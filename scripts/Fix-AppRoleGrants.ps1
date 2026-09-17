<#
.SYNOPSIS
Re-applies app_role table/function grants after restoring a PostgreSQL backup.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Load-DevEnv.ps1"

$psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
if (-not (Test-Path $psql)) {
    throw "psql not found at $psql"
}

$sqlFile = Join-Path $PSScriptRoot 'Fix-AppRoleGrants.sql'
$env:PGPASSWORD = $env:POSTGRES_SUPERUSER_PASSWORD

Write-Host 'Applying app_role grants...'
& $psql -h $env:POSTGRES_HOST -p $env:POSTGRES_HOST_PORT -U postgres -d $env:POSTGRES_DB -v ON_ERROR_STOP=1 -f $sqlFile

Write-Host 'Verifying app_role can read user_credentials...'
$verify = @'
SET ROLE app_role;
SELECT count(*) AS credential_rows FROM user_credentials;
RESET ROLE;
'@
$verifyFile = Join-Path $env:TEMP 'pts-verify-app-role.sql'
Set-Content -Path $verifyFile -Value $verify -Encoding UTF8
& $psql -h $env:POSTGRES_HOST -p $env:POSTGRES_HOST_PORT -U postgres -d $env:POSTGRES_DB -v ON_ERROR_STOP=1 -f $verifyFile

Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
Remove-Item $verifyFile -ErrorAction SilentlyContinue
Write-Host 'Done. Restart the backend if it is already running.'
