-- =====================================================================
-- Rollback Script: Schema Separation + Yearly Table Partitioning
-- Purpose: Restore database to original structure if needed
-- Created: 2026-03-07
-- WARNING: This will copy data back to public schema and may take time
-- =====================================================================

-- Step 1: Backup current state before rollback (optional but recommended)
-- =====================================================================
DO $$
BEGIN
    RAISE NOTICE 'Starting rollback process...';
    RAISE NOTICE 'Backup recommended: pg_dump -h localhost -p 5433 -U postgres -d cryptotrading > backup_before_rollback.sql';
END $$;

-- Step 2: Restore data to public.price_ticks if needed
-- =====================================================================
DO $$
DECLARE
    total_rows BIGINT;
BEGIN
    -- Check if we need to restore data
    IF EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'historical_collector') THEN
        
        RAISE NOTICE 'Restoring data to public.price_ticks...';
        
        -- Create public.price_ticks if it doesn't exist
        CREATE TABLE IF NOT EXISTS public.price_ticks (
            time TIMESTAMPTZ NOT NULL,
            symbol TEXT NOT NULL,
            price NUMERIC NOT NULL,
            volume NUMERIC NOT NULL,
            open NUMERIC,
            high NUMERIC,
            low NUMERIC,
            close NUMERIC,
            interval TEXT,
            CONSTRAINT uq_price_ticks_time_symbol_interval UNIQUE (time, symbol, interval)
        );
        
        CREATE INDEX IF NOT EXISTS idx_price_ticks_symbol_time 
            ON public.price_ticks(symbol, time DESC);
        
        -- Copy data from historical_collector tables back to public
        INSERT INTO public.price_ticks 
            (time, symbol, price, volume, open, high, low, close, interval)
        SELECT time, symbol, price, volume, open, high, low, close, interval
        FROM historical_collector.price_ticks
        ON CONFLICT (time, symbol, interval) DO NOTHING;
        
        GET DIAGNOSTICS total_rows = ROW_COUNT;
        RAISE NOTICE 'Restored % rows to public.price_ticks', total_rows;
        
        -- Restore data_gaps
        CREATE TABLE IF NOT EXISTS public.data_gaps (
            id BIGSERIAL PRIMARY KEY,
            symbol TEXT NOT NULL,
            interval TEXT NOT NULL,
            gap_start TIMESTAMPTZ NOT NULL,
            gap_end TIMESTAMPTZ NOT NULL,
            detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            filled_at TIMESTAMPTZ
        );
        
        CREATE INDEX IF NOT EXISTS idx_data_gaps_symbol_interval 
            ON public.data_gaps(symbol, interval);
        CREATE INDEX IF NOT EXISTS idx_data_gaps_unfilled 
            ON public.data_gaps(filled_at) WHERE filled_at IS NULL;
        
        INSERT INTO public.data_gaps 
            (id, symbol, interval, gap_start, gap_end, detected_at, filled_at)
        SELECT id, symbol, interval, gap_start, gap_end, detected_at, filled_at
        FROM historical_collector.data_gaps
        ON CONFLICT (id) DO NOTHING;
        
        GET DIAGNOSTICS total_rows = ROW_COUNT;
        RAISE NOTICE 'Restored % gap records', total_rows;
        
    ELSE
        RAISE NOTICE 'historical_collector schema not found. Nothing to rollback.';
    END IF;
END $$;

-- Step 3: Drop backward compatibility view in public
-- =====================================================================
DROP VIEW IF EXISTS public.price_ticks CASCADE;
RAISE NOTICE 'Dropped public.price_ticks compatibility view';

-- Step 4: Drop historical_collector schema and all objects
-- =====================================================================
DROP SCHEMA IF EXISTS historical_collector CASCADE;
RAISE NOTICE 'Dropped historical_collector schema';

-- Step 5: Drop other service schemas (only if empty)
-- =====================================================================
DROP SCHEMA IF EXISTS data_ingestor CASCADE;
DROP SCHEMA IF EXISTS analyzer CASCADE;
DROP SCHEMA IF EXISTS strategy CASCADE;
DROP SCHEMA IF EXISTS executor CASCADE;
DROP SCHEMA IF EXISTS risk_guard CASCADE;
DROP SCHEMA IF EXISTS notifier CASCADE;

RAISE NOTICE 'Dropped all service schemas';

-- Step 6: Verification
-- =====================================================================
DO $$
DECLARE
    public_count BIGINT;
BEGIN
    SELECT COUNT(*) INTO public_count FROM public.price_ticks;
    
    RAISE NOTICE '=== Rollback Verification ===';
    RAISE NOTICE 'public.price_ticks: % rows', public_count;
    RAISE NOTICE 'Rollback completed successfully!';
END $$;

-- =====================================================================
-- Rollback Complete!
-- =====================================================================
-- Next steps:
-- 1. Verify data in public.price_ticks
-- 2. Revert application code changes
-- 3. Update connection strings in appsettings.json
-- =====================================================================
