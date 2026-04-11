namespace FinancialLedger.Worker.Tests.Common;

internal static class LedgerTestSchema
{
    public const string Sql =
        """
        CREATE EXTENSION IF NOT EXISTS pgcrypto;

        CREATE TABLE IF NOT EXISTS public.virtual_accounts (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            environment VARCHAR(20) NOT NULL,
            base_currency VARCHAR(10) NOT NULL DEFAULT 'USDT',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (environment, base_currency)
        );

        CREATE TABLE IF NOT EXISTS public.test_sessions (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            account_id UUID NOT NULL REFERENCES public.virtual_accounts(id) ON DELETE CASCADE,
            algorithm_name VARCHAR(255) NOT NULL,
            initial_balance NUMERIC(20, 8) NOT NULL,
            start_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            end_time TIMESTAMPTZ,
            status VARCHAR(20) NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS public.ledger_entries (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            session_id UUID NOT NULL REFERENCES public.test_sessions(id) ON DELETE CASCADE,
            binance_transaction_id VARCHAR(255),
            type VARCHAR(50) NOT NULL,
            amount NUMERIC(20, 8) NOT NULL,
            symbol VARCHAR(20),
            timestamp TIMESTAMPTZ NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_ledger_entries_session_txn
            ON public.ledger_entries (session_id, binance_transaction_id)
            WHERE binance_transaction_id IS NOT NULL;

        CREATE TABLE IF NOT EXISTS public.ledger_equity_snapshots (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            session_id UUID NOT NULL REFERENCES public.test_sessions(id) ON DELETE CASCADE,
            trigger_transaction_id VARCHAR(255) NOT NULL,
            trigger_symbol VARCHAR(20),
            snapshot_time TIMESTAMPTZ NOT NULL,
            current_balance NUMERIC(20, 8) NOT NULL,
            holdings_market_value NUMERIC(20, 8) NOT NULL,
            total_equity NUMERIC(20, 8) NOT NULL,
            holdings_json JSONB NOT NULL DEFAULT '[]'::jsonb,
            event_type VARCHAR(20) NOT NULL DEFAULT 'SELL',
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_ledger_equity_snapshots_session_trigger
            ON public.ledger_equity_snapshots (session_id, trigger_transaction_id);
        """;
}
