-- =====================================================================
-- Migration Script: Schema Separation + Yearly Table Partitioning
-- Purpose: Restructure database to use schemas per service and 
--          yearly partitioned tables for price_ticks
-- Created: 2026-03-07
-- =====================================================================

-- Step 1: Create schemas for each microservice
-- =====================================================================
CREATE SCHEMA IF NOT EXISTS historical_collector;
CREATE SCHEMA IF NOT EXISTS data_ingestor;
CREATE SCHEMA IF NOT EXISTS analyzer;
CREATE SCHEMA IF NOT EXISTS strategy;
CREATE SCHEMA IF NOT EXISTS executor;
CREATE SCHEMA IF NOT EXISTS risk_guard;
CREATE SCHEMA IF NOT EXISTS notifier;

COMMENT ON SCHEMA historical_collector IS 'Historical data collection and backfill service';
COMMENT ON SCHEMA data_ingestor IS 'Real-time data ingestion from Binance WebSocket';
COMMENT ON SCHEMA analyzer IS 'Technical indicator computation service';
COMMENT ON SCHEMA strategy IS 'Trading strategy decision engine';
COMMENT ON SCHEMA executor IS 'Order execution service';
COMMENT ON SCHEMA risk_guard IS 'Risk validation service';
COMMENT ON SCHEMA notifier IS 'Notification and alerting service';

-- Step 2: Create yearly partitioned tables in historical_collector schema
-- =====================================================================

-- Function to create a yearly price_ticks table
CREATE OR REPLACE FUNCTION historical_collector.create_price_ticks_table_for_year(year_val INT)
RETURNS void AS $$
DECLARE
    table_name TEXT;
    start_date DATE;
    end_date DATE;
BEGIN
    table_name := 'price_' || year_val || '_ticks';
    start_date := make_date(year_val, 1, 1);
    end_date := make_date(year_val + 1, 1, 1);

    EXECUTE format('
        CREATE TABLE IF NOT EXISTS historical_collector.%I (
            time TIMESTAMPTZ NOT NULL,
            symbol TEXT NOT NULL,
            price NUMERIC NOT NULL,
            volume NUMERIC NOT NULL,
            open NUMERIC,
            high NUMERIC,
            low NUMERIC,
            close NUMERIC,
            interval TEXT NOT NULL,
            CONSTRAINT %I PRIMARY KEY (time, symbol, interval),
            CONSTRAINT %I CHECK (time >= %L::timestamptz AND time < %L::timestamptz)
        )', 
        table_name,
        'pk_' || table_name,
        'chk_' || table_name || '_year',
        start_date,
        end_date
    );

    -- Create indexes
    EXECUTE format('
        CREATE INDEX IF NOT EXISTS %I ON historical_collector.%I (symbol, time DESC);
    ', 'idx_' || table_name || '_symbol_time', table_name);

    EXECUTE format('
        CREATE INDEX IF NOT EXISTS %I ON historical_collector.%I (symbol, interval, time DESC);
    ', 'idx_' || table_name || '_symbol_interval_time', table_name);

    RAISE NOTICE 'Created table historical_collector.%', table_name;
END;
$$ LANGUAGE plpgsql;

-- Create tables for 2025 and 2026
SELECT historical_collector.create_price_ticks_table_for_year(2025);
SELECT historical_collector.create_price_ticks_table_for_year(2026);

-- Step 3: Create unified view for querying across all years
-- =====================================================================
CREATE OR REPLACE VIEW historical_collector.price_ticks AS
SELECT time, symbol, price, volume, open, high, low, close, interval
FROM historical_collector.price_2025_ticks
UNION ALL
SELECT time, symbol, price, volume, open, high, low, close, interval
FROM historical_collector.price_2026_ticks;

COMMENT ON VIEW historical_collector.price_ticks IS 'Unified view across all yearly price_ticks tables';

-- Step 4: Create helper function to get correct table name by timestamp
-- =====================================================================
CREATE OR REPLACE FUNCTION historical_collector.get_price_ticks_table_name(ts TIMESTAMPTZ)
RETURNS TEXT AS $$
DECLARE
    year_val INT;
BEGIN
    year_val := EXTRACT(YEAR FROM ts);
    RETURN 'historical_collector.price_' || year_val || '_ticks';
END;
$$ LANGUAGE plpgsql IMMUTABLE;

-- Step 5: Move data_gaps table to historical_collector schema
-- =====================================================================
CREATE TABLE IF NOT EXISTS historical_collector.data_gaps (
    id BIGSERIAL PRIMARY KEY,
    symbol TEXT NOT NULL,
    interval TEXT NOT NULL,
    gap_start TIMESTAMPTZ NOT NULL,
    gap_end TIMESTAMPTZ NOT NULL,
    detected_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    filled_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_data_gaps_symbol_interval 
    ON historical_collector.data_gaps(symbol, interval);
CREATE INDEX IF NOT EXISTS idx_data_gaps_unfilled 
    ON historical_collector.data_gaps(filled_at) WHERE filled_at IS NULL;

COMMENT ON TABLE historical_collector.data_gaps IS 'Tracks missing time ranges in historical data';

-- Step 6: Migrate existing data from public.price_ticks
-- =====================================================================
DO $$
DECLARE
    row_count_2025 BIGINT;
    row_count_2026 BIGINT;
    total_count BIGINT;
BEGIN
    -- Check if public.price_ticks exists
    IF EXISTS (SELECT 1 FROM information_schema.tables 
               WHERE table_schema = 'public' AND table_name = 'price_ticks') THEN
        
        RAISE NOTICE 'Starting data migration from public.price_ticks...';
        
        -- Migrate 2025 data
        INSERT INTO historical_collector.price_2025_ticks 
            (time, symbol, price, volume, open, high, low, close, interval)
        SELECT time, symbol, price, volume, open, high, low, close, interval
        FROM public.price_ticks
        WHERE time >= '2025-01-01'::timestamptz 
          AND time < '2026-01-01'::timestamptz
        ON CONFLICT (time, symbol, interval) DO NOTHING;
        
        GET DIAGNOSTICS row_count_2025 = ROW_COUNT;
        RAISE NOTICE 'Migrated % rows to price_2025_ticks', row_count_2025;
        
        -- Migrate 2026 data
        INSERT INTO historical_collector.price_2026_ticks 
            (time, symbol, price, volume, open, high, low, close, interval)
        SELECT time, symbol, price, volume, open, high, low, close, interval
        FROM public.price_ticks
        WHERE time >= '2026-01-01'::timestamptz 
          AND time < '2027-01-01'::timestamptz
        ON CONFLICT (time, symbol, interval) DO NOTHING;
        
        GET DIAGNOSTICS row_count_2026 = ROW_COUNT;
        RAISE NOTICE 'Migrated % rows to price_2026_ticks', row_count_2026;
        
        -- Migrate data_gaps if exists
        IF EXISTS (SELECT 1 FROM information_schema.tables 
                   WHERE table_schema = 'public' AND table_name = 'data_gaps') THEN
            INSERT INTO historical_collector.data_gaps 
                (id, symbol, interval, gap_start, gap_end, detected_at, filled_at)
            SELECT id, symbol, interval, gap_start, gap_end, detected_at, filled_at
            FROM public.data_gaps
            ON CONFLICT (id) DO NOTHING;
            
            GET DIAGNOSTICS total_count = ROW_COUNT;
            RAISE NOTICE 'Migrated % gap records', total_count;
            
            -- Update sequence
            PERFORM setval('historical_collector.data_gaps_id_seq', 
                          (SELECT COALESCE(MAX(id), 0) FROM historical_collector.data_gaps));
        END IF;
        
        RAISE NOTICE 'Migration completed successfully!';
        RAISE NOTICE 'Total rows migrated: %', row_count_2025 + row_count_2026;
        
    ELSE
        RAISE NOTICE 'No public.price_ticks table found. Skipping migration.';
    END IF;
END $$;

-- Step 7: Create backward compatibility view in public schema (optional)
-- =====================================================================
CREATE OR REPLACE VIEW public.price_ticks AS
SELECT * FROM historical_collector.price_ticks;

COMMENT ON VIEW public.price_ticks IS 'Backward compatibility view - redirects to historical_collector.price_ticks';

-- Step 8: Verification queries
-- =====================================================================
DO $$
DECLARE
    count_2025 BIGINT;
    count_2026 BIGINT;
    count_view BIGINT;
BEGIN
    SELECT COUNT(*) INTO count_2025 FROM historical_collector.price_2025_ticks;
    SELECT COUNT(*) INTO count_2026 FROM historical_collector.price_2026_ticks;
    SELECT COUNT(*) INTO count_view FROM historical_collector.price_ticks;
    
    RAISE NOTICE '=== Migration Verification ===';
    RAISE NOTICE 'price_2025_ticks: % rows', count_2025;
    RAISE NOTICE 'price_2026_ticks: % rows', count_2026;
    RAISE NOTICE 'Unified view: % rows', count_view;
    RAISE NOTICE 'Match: %', CASE WHEN count_view = count_2025 + count_2026 THEN 'YES ✓' ELSE 'NO ✗' END;
END $$;

-- Step 9: Grant permissions (adjust as needed for your setup)
-- =====================================================================
-- Example: Grant usage on schemas to application role
-- GRANT USAGE ON SCHEMA historical_collector TO cryptotrading_app;
-- GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA historical_collector TO cryptotrading_app;
-- GRANT SELECT ON historical_collector.price_ticks TO cryptotrading_app;

-- Step 10: Create function to ensure table exists before insert
-- =====================================================================
CREATE OR REPLACE FUNCTION historical_collector.ensure_price_ticks_table_exists(ts TIMESTAMPTZ)
RETURNS TEXT AS $$
DECLARE
    year_val INT;
    table_name TEXT;
    table_exists BOOLEAN;
BEGIN
    year_val := EXTRACT(YEAR FROM ts);
    table_name := 'price_' || year_val || '_ticks';
    
    -- Check if table exists
    SELECT EXISTS (
        SELECT 1 FROM information_schema.tables 
        WHERE table_schema = 'historical_collector' 
        AND table_name = table_name
    ) INTO table_exists;
    
    -- Create if not exists
    IF NOT table_exists THEN
        PERFORM historical_collector.create_price_ticks_table_for_year(year_val);
        
        -- Refresh the unified view to include the new table
        EXECUTE '
            CREATE OR REPLACE VIEW historical_collector.price_ticks AS ' || (
                SELECT string_agg(
                    'SELECT time, symbol, price, volume, open, high, low, close, interval FROM historical_collector.' || t.table_name,
                    ' UNION ALL '
                )
                FROM information_schema.tables t
                WHERE t.table_schema = 'historical_collector'
                  AND t.table_name LIKE 'price_%_ticks'
                  AND t.table_type = 'BASE TABLE'
                ORDER BY t.table_name
            );
    END IF;
    
    RETURN 'historical_collector.' || table_name;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION historical_collector.ensure_price_ticks_table_exists IS 'Auto-creates yearly table if needed when inserting data';

-- =====================================================================
-- Migration Complete!
-- =====================================================================
-- Next steps:
-- 1. Verify data integrity with queries in Step 8
-- 2. Update application code to use new schema
-- 3. Optionally rename/backup old public.price_ticks table
-- 4. Update connection strings in appsettings.json if needed
-- =====================================================================

RAISE NOTICE '=== Migration script completed successfully! ===';
