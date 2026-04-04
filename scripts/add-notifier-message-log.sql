-- ============================================================
-- Notifier Telegram Message Log
-- Persists all successfully sent Telegram messages for UI and audit.
-- ============================================================

CREATE TABLE IF NOT EXISTS public.notifier_message_logs (
    id                    BIGSERIAL PRIMARY KEY,
    channel               VARCHAR(20) NOT NULL DEFAULT 'telegram',
    category              VARCHAR(50) NOT NULL,
    summary               TEXT NOT NULL,
    message_text          TEXT NOT NULL,
    sent_at_utc           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    external_message_id   TEXT,
    created_at_utc        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_notifier_message_logs_sent_at
    ON public.notifier_message_logs (sent_at_utc DESC);

CREATE INDEX IF NOT EXISTS idx_notifier_message_logs_category_sent_at
    ON public.notifier_message_logs (category, sent_at_utc DESC);
