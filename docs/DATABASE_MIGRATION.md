# Database Schema Partitioning Migration Guide

## Overview

This migration restructures the database to:
1. **Separate schemas for each microservice** - Provides clear boundaries and easier management
2. **Yearly partitioned tables** - Prevents single table from growing too large
3. **Automatic table creation** - New yearly tables are created automatically when needed

## Architecture Changes

### Before Migration
```
public
  └── price_ticks (all data in one table)
  └── data_gaps
```

### After Migration
```
historical_collector
  ├── price_2025_ticks
  ├── price_2026_ticks
  ├── price_2027_ticks (auto-created when needed)
  ├── price_ticks (view - UNION ALL of yearly tables)
  └── data_gaps

data_ingestor
analyzer
strategy
executor
risk_guard
notifier
```

## Benefits

1. **Reduced table size** - Each year has its own table, improving query performance
2. **Easy data archival** - Drop old yearly tables when no longer needed
3. **Clear service boundaries** - Each service has its own schema
4. **Automatic scaling** - New tables created automatically for future years
5. **Backward compatibility** - Unified view allows querying across all years

## Migration Steps

### Step 1: Backup Your Database

```powershell
# Create a full backup before migration
$env:PGPASSWORD = 'postgres'
pg_dump -h localhost -p 5433 -U postgres -d cryptotrading > backup_before_migration.sql
$env:PGPASSWORD = ''
```

### Step 2: Run the Migration Script

```powershell
# Option A: Using the PowerShell wrapper (recommended)
cd d:\Code\CryptoAlgorithmicTrading
.\scripts\run-migration.ps1 -DbPassword postgres

# Option B: Direct SQL execution
$psql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe'
$env:PGPASSWORD = 'postgres'
& $psql -h localhost -p 5433 -U postgres -d cryptotrading -f .\scripts\migrate-to-schema-partitioning.sql
$env:PGPASSWORD = ''
```

The migration script will:
- ✅ Create 7 new schemas (one per service)
- ✅ Create yearly tables for 2025 and 2026
- ✅ Migrate all existing data from `public.price_ticks`
- ✅ Create helper functions for automatic table management
- ✅ Create a unified view for backward compatibility
- ✅ Verify data integrity

### Step 3: Verify Migration Success

```sql
-- Check row counts match
SELECT 
    'price_2025_ticks' as table_name,
    COUNT(*) as row_count
FROM historical_collector.price_2025_ticks
UNION ALL
SELECT 
    'price_2026_ticks',
    COUNT(*)
FROM historical_collector.price_2026_ticks
UNION ALL
SELECT 
    'unified_view',
    COUNT(*)
FROM historical_collector.price_ticks;

-- Check data ranges
SELECT 
    symbol,
    MIN(time) as earliest,
    MAX(time) as latest,
    COUNT(*) as total_candles
FROM historical_collector.price_ticks
GROUP BY symbol
ORDER BY symbol;
```

### Step 4: Test Application Services

```powershell
# Test HistoricalCollector (should write to correct yearly table)
dotnet run --project .\src\Services\HistoricalCollector\HistoricalCollector.Worker\HistoricalCollector.Worker.csproj

# Test DataIngestor (should write real-time data)
dotnet run --project .\src\Services\DataIngestor\Ingestor.Worker\Ingestor.Worker.csproj
```

Check logs for successful insertions into yearly tables.

### Step 5: Monitor for Issues

Watch for these indicators that migration was successful:
- ✅ No errors in application logs
- ✅ New data appears in correct yearly tables
- ✅ Query performance remains good or improves
- ✅ No duplicate data

### Step 6: Clean Up (After Validation Period)

After 1-2 weeks of successful operation:

```sql
-- Option A: Rename old table for safety
ALTER TABLE public.price_ticks RENAME TO price_ticks_old_backup;

-- Option B: Drop old table (only if confident!)
-- DROP TABLE public.price_ticks CASCADE;
```

## Rollback Procedure

If you encounter issues, rollback is available:

```powershell
# Restore from backup
$env:PGPASSWORD = 'postgres'
psql -h localhost -p 5433 -U postgres -d postgres -c "DROP DATABASE cryptotrading;"
psql -h localhost -p 5433 -U postgres -d postgres -c "CREATE DATABASE cryptotrading;"
psql -h localhost -p 5433 -U postgres -d cryptotrading < backup_before_migration.sql
$env:PGPASSWORD = ''

# OR use the rollback script (copies data back to public schema)
.\scripts\run-rollback.ps1 -DbPassword postgres
```

## How the New System Works

### Automatic Table Creation

When inserting data for a new year (e.g., 2027), the system automatically:
1. Detects the year from the timestamp
2. Calls `historical_collector.ensure_price_ticks_table_exists()`
3. Creates `price_2027_ticks` if it doesn't exist
4. Updates the unified view to include the new table
5. Proceeds with the insert

### Querying Data

```csharp
// In your application code - no changes needed!
// The unified view handles routing automatically
var query = "SELECT * FROM historical_collector.price_ticks WHERE symbol = @Symbol";

// Or query specific yearly table for better performance
var query2025 = "SELECT * FROM historical_collector.price_2025_ticks WHERE symbol = @Symbol";
```

### Helper Functions

```csharp
// Use the helper class in your repositories
using CryptoTrading.Shared.Database;

// Get table name for a specific timestamp
var tableName = PriceTicksTableHelper.GetTableName(DateTime.UtcNow);
// Returns: "historical_collector.price_2026_ticks"

// Group data by year for batch insert
var grouped = PriceTicksTableHelper.GroupByYear(priceTicks);
// Returns: Dictionary<int, List<PriceTick>> grouped by year

// Ensure table exists before insert
await PriceTicksTableHelper.EnsureTableExistsAsync(connection, timestamp);
```

## Performance Considerations

### Query Optimization

```sql
-- ✅ GOOD: Query specific year when possible
SELECT * FROM historical_collector.price_2025_ticks 
WHERE symbol = 'BTCUSDT' AND time > '2025-03-01';

-- ⚠️ OK: Query across years when needed (uses unified view)
SELECT * FROM historical_collector.price_ticks 
WHERE symbol = 'BTCUSDT' AND time > '2025-01-01';

-- ❌ AVOID: Queries without time filters on unified view
SELECT * FROM historical_collector.price_ticks WHERE symbol = 'BTCUSDT';
```

### Maintenance

```sql
-- Analyze tables periodically for optimal performance
ANALYZE historical_collector.price_2025_ticks;
ANALYZE historical_collector.price_2026_ticks;

-- Vacuum tables to reclaim space
VACUUM ANALYZE historical_collector.price_2025_ticks;

-- Check table sizes
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size
FROM pg_tables
WHERE schemaname = 'historical_collector'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;
```

## Troubleshooting

### Issue: Migration script fails midway

**Solution**: Check the error message. Most likely causes:
- Connection string incorrect
- Insufficient permissions
- Disk space full

Rollback and fix the issue before retrying.

### Issue: Application can't find tables

**Solution**: Verify connection string in `appsettings.json` is correct.
Check that schemas were created:

```sql
SELECT schema_name FROM information_schema.schemata 
WHERE schema_name IN ('historical_collector', 'data_ingestor');
```

### Issue: Duplicate data after migration

**Solution**: The unique constraint prevents duplicates. Check if data was inserted twice during migration.

```sql
-- Find duplicates
SELECT time, symbol, interval, COUNT(*) 
FROM historical_collector.price_ticks
GROUP BY time, symbol, interval
HAVING COUNT(*) > 1;
```

### Issue: Performance degradation

**Solution**: Ensure indexes are created on yearly tables:

```sql
-- Verify indexes exist
SELECT tablename, indexname 
FROM pg_indexes 
WHERE schemaname = 'historical_collector';

-- Recreate if missing
CREATE INDEX IF NOT EXISTS idx_price_2025_ticks_symbol_time 
    ON historical_collector.price_2025_ticks(symbol, time DESC);
```

## Future Expansion

### Adding a New Service Schema

```sql
-- Create schema for new service
CREATE SCHEMA IF NOT EXISTS new_service_name;
COMMENT ON SCHEMA new_service_name IS 'Description of the service';

-- Grant permissions
GRANT USAGE ON SCHEMA new_service_name TO cryptotrading_app;
```

### Archiving Old Data

```sql
-- Archive 2025 data to backup (after 2027+)
pg_dump -h localhost -p 5433 -U postgres -d cryptotrading \
    -t historical_collector.price_2025_ticks \
    > archive_2025_ticks.sql

-- Drop old table to free space
DROP TABLE historical_collector.price_2025_ticks;

-- Recreate unified view without 2025
CREATE OR REPLACE VIEW historical_collector.price_ticks AS
SELECT * FROM historical_collector.price_2026_ticks
UNION ALL
SELECT * FROM historical_collector.price_2027_ticks;
```

## Support

If you encounter issues:
1. Check application logs for detailed error messages
2. Verify database connection and permissions
3. Review this guide's troubleshooting section
4. Use the rollback procedure if needed

## Summary Checklist

- [ ] Backup database completed
- [ ] Migration script executed successfully
- [ ] Data verification passed
- [ ] Application services tested
- [ ] Monitoring for 1-2 weeks
- [ ] Old table cleaned up (optional)

---

**Migration Date**: 2026-03-07  
**Database Version**: PostgreSQL 17 with TimescaleDB  
**Expected Downtime**: < 5 minutes for migration script execution  
**Rollback Available**: Yes
