-- Add system_settings table for configurable system parameters (timezone, etc.)
CREATE TABLE IF NOT EXISTS public.system_settings (
    key          TEXT        PRIMARY KEY,
    value        TEXT        NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_by   TEXT
);

-- Seed default timezone
INSERT INTO public.system_settings (key, value, updated_at_utc)
VALUES ('timezone', 'UTC', NOW())
ON CONFLICT (key) DO NOTHING;
