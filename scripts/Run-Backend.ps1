<#
.SYNOPSIS
Runs PTS.Host with the local development environment loaded.

.EXAMPLE
    .\scripts\Run-Backend.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Load-DevEnv.ps1')

$hostProject = Join-Path $PSScriptRoot '..\src\Backend\Host\PTS.Host'
dotnet run --project $hostProject
