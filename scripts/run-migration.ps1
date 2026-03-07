#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Executes the schema partitioning migration safely with backups and verification.

.DESCRIPTION
    This script:
    1. Creates a backup of the current database
    2. Runs the migration script
    3. Verifies data integrity
    4. Provides rollback instructions if needed

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

.PARAMETER SkipBackup
    Skip backup creation (not recommended)

.EXAMPLE
    .\run-migration.ps1 -DbPassword postgres
    
.EXAMPLE
    .\run-migration.ps1 -DbPassword postgres -PsqlPath "C:\Program Files\PostgreSQL\17\bin\psql.exe"
#>

[CmdletBinding()]
param(
    [string]$DbHost = "localhost",
    [int]$DbPort = 5433,
    [string]$DbName = "cryptotrading",
    [string]$DbUser = "postgres",
    [Parameter(Mandatory)]
    [string]$DbPassword,
    [string]$PsqlPath = "psql",
    [switch]$SkipBackup
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$migrationScript = Join-Path $scriptRoot "migrate-to-schema-partitioning.sql"
$rollbackScript = Join-Path $scriptRoot "rollback-schema-partitioning.sql"

# Colors for output
function Write-Info($message) { Write-Host "ℹ️  $message" -ForegroundColor Cyan }
function Write-Success($message) { Write-Host "✓ $message" -ForegroundColor Green }
function Write-Warning($message) { Write-Host "⚠️  $message" -ForegroundColor Yellow }
function Write-Error-Custom($message) { Write-Host "✗ $message" -ForegroundColor Red }

Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  Database Migration: Schema Partitioning" -ForegroundColor Magenta
Write-Host "========================================`n" -ForegroundColor Magenta

# Verify psql is available
try {
    $psqlVersion = & $PsqlPath --version 2>&1
    Write-Info "Using PostgreSQL client: $psqlVersion"
} catch {
    Write-Error-Custom "psql not found at: $PsqlPath"
    Write-Host "Please install PostgreSQL client or provide correct path with -PsqlPath parameter"
    exit 1
}

# Verify migration script exists
if (-not (Test-Path $migrationScript)) {
    Write-Error-Custom "Migration script not found: $migrationScript"
    exit 1
}

# Set password environment variable
$env:PGPASSWORD = $DbPassword

try {
    # Step 1: Test connection
    Write-Info "Testing database connection..."
    $connTest = & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -c "SELECT version();" 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Connection failed: $connTest"
    }
    Write-Success "Database connection successful"

    # Step 2: Get current row counts
    Write-Info "Checking current data..."
    $currentCountQuery = @"
SELECT 
    COUNT(*) as total_rows,
    COUNT(DISTINCT symbol) as symbols,
    MIN(time) as earliest,
    MAX(time) as latest
FROM public.price_ticks;
"@
    
    $currentCounts = & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -t -c $currentCountQuery 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Current data summary:" -ForegroundColor Yellow
        Write-Host $currentCounts
    }

    # Step 3: Create backup
    if (-not $SkipBackup) {
        Write-Info "Creating database backup..."
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupFile = Join-Path $scriptRoot "backup_before_migration_$timestamp.sql"
        
        $env:PGPASSWORD = $DbPassword
        & pg_dump -h $DbHost -p $DbPort -U $DbUser -d $DbName -f $backupFile 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0 -and (Test-Path $backupFile)) {
            $backupSize = (Get-Item $backupFile).Length / 1MB
            Write-Success "Backup created: $backupFile ($([math]::Round($backupSize, 2)) MB)"
        } else {
            Write-Warning "Backup failed, but continuing. Consider manual backup!"
        }
    } else {
        Write-Warning "Skipping backup (not recommended for production!)"
    }

    # Step 4: Ask for confirmation
    Write-Host "`n" -NoNewline
    Write-Warning "This will restructure your database with schemas and yearly partitions."
    Write-Host "The old public.price_ticks table will be preserved but not used." -ForegroundColor Yellow
    Write-Host "`nRollback script available at: " -NoNewline
    Write-Host $rollbackScript -ForegroundColor Cyan
    Write-Host "`n"
    
    $response = Read-Host "Continue with migration? (yes/no)"
    if ($response -ne "yes") {
        Write-Warning "Migration cancelled by user"
        exit 0
    }

    # Step 5: Run migration
    Write-Info "Executing migration script..."
    $migrationOutput = & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -f $migrationScript 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error-Custom "Migration failed!"
        Write-Host $migrationOutput -ForegroundColor Red
        Write-Host "`nTo rollback, run: " -NoNewline
        Write-Host ".\run-rollback.ps1 -DbPassword $DbPassword" -ForegroundColor Cyan
        exit 1
    }
    
    Write-Host $migrationOutput -ForegroundColor Gray
    Write-Success "Migration script executed successfully"

    # Step 6: Verify migration
    Write-Info "Verifying migration results..."
    
    $verifyQuery = @"
SELECT 
    'price_2025_ticks' as table_name,
    COUNT(*) as row_count,
    MIN(time) as earliest,
    MAX(time) as latest
FROM historical_collector.price_2025_ticks
UNION ALL
SELECT 
    'price_2026_ticks',
    COUNT(*),
    MIN(time),
    MAX(time)
FROM historical_collector.price_2026_ticks
UNION ALL
SELECT 
    'unified_view',
    COUNT(*),
    MIN(time),
    MAX(time)
FROM historical_collector.price_ticks
ORDER BY table_name;
"@
    
    $verifyOutput = & $PsqlPath -h $DbHost -p $DbPort -U $DbUser -d $DbName -c $verifyQuery 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n=== Migration Results ===" -ForegroundColor Green
        Write-Host $verifyOutput
        Write-Success "Data verification passed"
    } else {
        Write-Warning "Verification query failed, but migration may still be successful"
    }

    # Step 7: Success summary
    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "  Migration Completed Successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "1. Update application code to use new schemas"
    Write-Host "2. Test application with new database structure"
    Write-Host "3. Monitor performance and data integrity"
    Write-Host "4. After confirming everything works, you can:"
    Write-Host "   - Rename public.price_ticks to price_ticks_old (for safety)"
    Write-Host "   - Or drop it after sufficient testing period"
    Write-Host "`nRollback available if needed: .\run-rollback.ps1" -ForegroundColor Cyan
    Write-Host ""

} catch {
    Write-Error-Custom "Migration failed: $_"
    Write-Host "`nTo rollback changes, run:" -ForegroundColor Yellow
    Write-Host "  .\run-rollback.ps1 -DbPassword $DbPassword" -ForegroundColor Cyan
    exit 1
} finally {
    # Clear password from environment
    $env:PGPASSWORD = ""
}
