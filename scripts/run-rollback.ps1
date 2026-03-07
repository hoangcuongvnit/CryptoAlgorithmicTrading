#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Rolls back the schema partitioning migration.

.DESCRIPTION
    Restores the database to use public.price_ticks instead of partitioned tables.

.PARAMETER DbHost
    PostgreSQL host (default: localhost)

.PARAMETER DbPort
    PostgreSQL port (default: 5433)

.PARAMETER DbName
    Database name (default: cryptotrading)

.PARAMETER DbUser
    Database user (default: postgres)

.PARAMETER DbPassword
    Database password

.PARAMETER PsqlPath
    Full path to psql.exe executable

.EXAMPLE
    .\run-rollback.ps1 -DbPassword postgres
#>

[CmdletBinding()]
param(
    [string]$DbHost = "localhost",
    [int]$DbPort = 5433,
    [string]$DbName = "cryptotrading",
    [string]$DbUser = "postgres",
    [Parameter(Mandatory)]
    [string]$DbPassword,
    [string]$PsqlPath = "psql"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$rollbackScript = Join-Path $scriptRoot "rollback-schema-partitioning.sql"

Write-Host "`n========================================" -ForegroundColor Red
Write-Host "  Database Rollback: Schema Partitioning" -ForegroundColor Red
Write-Host "========================================`n" -ForegroundColor Red

Write-Warning "This will rollback the schema partitioning migration."
Write-Warning "Data from partitioned tables will be copied back to public.price_ticks"
Write-Host ""

$response = Read-Host "Continue with rollback? (yes/no)"
if ($response -ne "yes") {
    Write-Host "Rollback cancelled" -ForegroundColor Yellow
    exit 0
}

$env:PGPASSWORD = $DbPassword

try {
    Write-Host "Executing rollback script..." -ForegroundColor Cyan
    $output = & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -f $rollbackScript 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Rollback failed!" -ForegroundColor Red
        Write-Host $output -ForegroundColor Red
        exit 1
    }
    
    Write-Host $output -ForegroundColor Gray
    Write-Host "`nRollback completed successfully!" -ForegroundColor Green
    
} finally {
    $env:PGPASSWORD = ""
}
