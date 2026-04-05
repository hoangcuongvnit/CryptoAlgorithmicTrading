using CryptoTrading.Shared.DTOs;
using Dapper;
using Npgsql;

namespace Executor.API.Infrastructure;

public sealed class OrderRepository
{
    private readonly string _connectionString;
    private readonly ILogger<OrderRepository> _logger;

    public OrderRepository(IConfiguration configuration, ILogger<OrderRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured");
        _logger = logger;
    }

    public async Task PersistAsync(OrderRequest request, OrderResult result, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO orders (
                id, time, symbol, side, order_type, quantity, price,
                filled_price, filled_qty, stop_loss, take_profit,
                strategy, success, error_msg, status,
                session_id, session_phase, is_reduce_only,
                forced_liquidation, liquidation_reason)
            VALUES (
                @Id, @Time, @Symbol, @Side, @OrderType, @Quantity, @Price,
                @FilledPrice, @FilledQty, @StopLoss, @TakeProfit,
                @Strategy, @Success, @ErrorMessage, @Status,
                @SessionId, @SessionPhase, @IsReduceOnly,
                @ForcedLiquidation, @LiquidationReason);
            """;

        var rowId = Guid.NewGuid();

        var parameters = new
        {
            Id = rowId,
            Time = result.Timestamp,
            Symbol = request.Symbol,
            Side = request.Side.ToString(),
            OrderType = request.Type.ToString(),
            Quantity = request.Quantity,
            Price = request.Price == 0 ? (decimal?)null : request.Price,
            FilledPrice = result.FilledPrice == 0 ? (decimal?)null : result.FilledPrice,
            FilledQty = result.FilledQty == 0 ? (decimal?)null : result.FilledQty,
            StopLoss = request.StopLoss == 0 ? (decimal?)null : request.StopLoss,
            TakeProfit = request.TakeProfit == 0 ? (decimal?)null : request.TakeProfit,
            Strategy = request.StrategyName,
            Success = result.Success,
            ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage) ? null : result.ErrorMessage,
            Status = result.Success ? "OPEN" : "FAILED",
            SessionId = request.SessionId,
            SessionPhase = request.SessionPhase?.ToString(),
            IsReduceOnly = request.IsReduceOnly,
            ForcedLiquidation = result.ForcedLiquidation,
            LiquidationReason = result.LiquidationReason.ToString()
        };

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist order execution for {Symbol}", request.Symbol);
            throw;
        }
    }

    public async Task UpdateRealizedPnLAsync(
        string orderId,
        decimal realizedPnL,
        decimal exitPrice,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE orders
            SET status       = 'CLOSED',
                realized_pnl = @RealizedPnL,
                exit_price   = @ExitPrice,
                exit_time    = @ExitTime,
                roe_percent  = CASE WHEN filled_price > 0 AND filled_qty > 0
                                   THEN @RealizedPnL / (filled_price * filled_qty) * 100
                                   ELSE NULL
                               END
            WHERE id = @Id::uuid;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { Id = orderId, RealizedPnL = realizedPnL, ExitPrice = exitPrice, ExitTime = DateTime.UtcNow },
                cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update realized P&L for order {OrderId}", orderId);
        }
    }

    public async Task<IReadOnlyList<OrderSummary>> GetRecentOrdersAsync(
        int limit,
        CancellationToken cancellationToken,
        string? symbol = null)
    {
        var sql = """
            SELECT id           AS OrderId,
                   symbol       AS Symbol,
                   side         AS Side,
                   order_type   AS OrderType,
                   quantity     AS Quantity,
                   price        AS EntryPrice,
                   filled_price AS FilledPrice,
                   filled_qty   AS FilledQty,
                   stop_loss    AS StopLoss,
                   take_profit  AS TakeProfit,
                   status       AS Status,
                   realized_pnl AS RealizedPnL,
                   roe_percent  AS RoePercent,
                   success      AS Success,
                   error_msg    AS ErrorMessage,
                   strategy     AS Strategy,
                   time         AS CreatedAt
            FROM public.orders
            """
            + (symbol is not null ? " WHERE symbol = @Symbol" : "")
            + " ORDER BY time DESC LIMIT @Limit;";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<OrderSummary>(
                new CommandDefinition(sql, new { Symbol = symbol, Limit = limit }, cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch recent orders");
            return [];
        }
    }

    public async Task<IReadOnlyList<OrderSummary>> GetOrdersByTimeRangeAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id           AS OrderId,
                   symbol       AS Symbol,
                   side         AS Side,
                   order_type   AS OrderType,
                   quantity     AS Quantity,
                   price        AS EntryPrice,
                   filled_price AS FilledPrice,
                   filled_qty   AS FilledQty,
                   stop_loss    AS StopLoss,
                   take_profit  AS TakeProfit,
                   status       AS Status,
                   realized_pnl AS RealizedPnL,
                   roe_percent  AS RoePercent,
                   success      AS Success,
                   error_msg    AS ErrorMessage,
                   strategy     AS Strategy,
                   time         AS CreatedAt
            FROM public.orders
            WHERE symbol = @Symbol AND time >= @From AND time < @To
            ORDER BY time ASC
            LIMIT 200;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<OrderSummary>(
                new CommandDefinition(sql, new { Symbol = symbol, From = from, To = to }, cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch orders by time range for {Symbol}", symbol);
            return [];
        }
    }

    public sealed record OrderSummary(
        Guid OrderId,
        string Symbol,
        string Side,
        string OrderType,
        decimal Quantity,
        decimal? EntryPrice,
        decimal? FilledPrice,
        decimal? FilledQty,
        decimal? StopLoss,
        decimal? TakeProfit,
        string Status,
        decimal? RealizedPnL,
        decimal? RoePercent,
        bool Success,
        string? ErrorMessage,
        string? Strategy,
        DateTime CreatedAt);

    // ── Daily Report DTOs ────────────────────────────────────────────────────

    public sealed record DailyReportSummary(
        DateTime Date,
        int TotalTrades,
        int BuyOrders,
        int SellOrders,
        int WinTrades,
        int LossTrades,
        decimal WinRate,
        decimal RealizedPnL,
        decimal GrossProfit,
        decimal GrossLoss,
        decimal ProfitFactor,
        decimal AvgWin,
        decimal AvgLoss,
        int MarketOrders,
        int LimitOrders,
        int FailedOrders);

    public sealed record DailySymbolBreakdown(
        string Symbol,
        int BuyCount,
        int SellCount,
        decimal BuyQty,
        decimal SellQty,
        decimal? AvgBuyPrice,
        decimal? AvgSellPrice,
        decimal RealizedPnL,
        int WinCount,
        int LossCount,
        DateTime? LastTradeTime);

    public sealed record TradeTimeRecord(
        Guid OrderId,
        string Symbol,
        string Side,
        decimal? RealizedPnL,
        DateTime OpenTime,
        DateTime? CloseTime,
        double? HoldingMinutes);

    public sealed record HourlyTradeBucket(int Hour, int BuyCount, int SellCount);

    // ── 4-Hour Session Report DTOs ───────────────────────────────────────────

    public sealed record SessionReportRow(
        string SessionId,
        int SessionNumber,
        DateTime SessionStartUtc,
        DateTime SessionEndUtc,
        int TotalOrders,
        int BuyCount,
        int SellCount,
        int RejectedCount,
        int WinTrades,
        int LossTrades,
        decimal RealizedPnL,
        int DistinctSymbols,
        string SymbolsCsv,
        bool IsFlatAtClose);

    public sealed record SessionSymbolRow(
        string SessionId,
        string Symbol,
        int BuyCount,
        int SellCount,
        decimal BuyQty,
        decimal SellQty,
        decimal? AvgBuyPrice,
        decimal? AvgSellPrice,
        decimal RealizedPnL,
        int WinTrades,
        int LossTrades);

    public sealed record SessionEquityPoint(
        string SessionId,
        int SessionNumber,
        DateTime SessionStartUtc,
        decimal SessionPnL,
        decimal CumulativePnL);

    // ── Daily Report Queries ─────────────────────────────────────────────────

    public async Task<DailyReportSummary> GetDailyReportAsync(DateTime date, CancellationToken cancellationToken)
    {
        var startUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var endUtc = startUtc.AddDays(1);

        const string sql = """
            SELECT
                COUNT(*)                                          AS total_trades,
                COUNT(*) FILTER (WHERE side = 'Buy')             AS buy_orders,
                COUNT(*) FILTER (WHERE side = 'Sell')            AS sell_orders,
                COUNT(*) FILTER (WHERE realized_pnl > 0)         AS win_trades,
                COUNT(*) FILTER (WHERE realized_pnl < 0)         AS loss_trades,
                COALESCE(SUM(realized_pnl), 0)                   AS realized_pnl,
                COALESCE(SUM(realized_pnl) FILTER (WHERE realized_pnl > 0), 0) AS gross_profit,
                COALESCE(ABS(SUM(realized_pnl) FILTER (WHERE realized_pnl < 0)), 0) AS gross_loss,
                COALESCE(AVG(realized_pnl) FILTER (WHERE realized_pnl > 0), 0) AS avg_win,
                COALESCE(AVG(realized_pnl) FILTER (WHERE realized_pnl < 0), 0) AS avg_loss,
                COUNT(*) FILTER (WHERE order_type = 'Market')    AS market_orders,
                COUNT(*) FILTER (WHERE order_type = 'Limit')     AS limit_orders,
                COUNT(*) FILTER (WHERE success = false)          AS failed_orders
            FROM public.orders
            WHERE time >= @Start AND time < @End
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var row = await connection.QuerySingleAsync<dynamic>(
                new CommandDefinition(sql, new { Start = startUtc, End = endUtc }, cancellationToken: cancellationToken));

            int total = (int)(long)row.total_trades;
            int wins = (int)(long)row.win_trades;
            int losses = (int)(long)row.loss_trades;
            decimal gp = (decimal)row.gross_profit;
            decimal gl = (decimal)row.gross_loss;

            return new DailyReportSummary(
                Date: date.Date,
                TotalTrades: total,
                BuyOrders: (int)(long)row.buy_orders,
                SellOrders: (int)(long)row.sell_orders,
                WinTrades: wins,
                LossTrades: losses,
                WinRate: total > 0 ? decimal.Round((decimal)wins / total, 4) : 0m,
                RealizedPnL: decimal.Round((decimal)row.realized_pnl, 4),
                GrossProfit: decimal.Round(gp, 4),
                GrossLoss: decimal.Round(gl, 4),
                ProfitFactor: gl > 0 ? decimal.Round(gp / gl, 4) : (gp > 0 ? 999m : 0m),
                AvgWin: decimal.Round((decimal)row.avg_win, 4),
                AvgLoss: decimal.Round((decimal)row.avg_loss, 4),
                MarketOrders: (int)(long)row.market_orders,
                LimitOrders: (int)(long)row.limit_orders,
                FailedOrders: (int)(long)row.failed_orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch daily report for {Date}", date.Date);
            return new DailyReportSummary(date.Date, 0, 0, 0, 0, 0, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0, 0, 0);
        }
    }

    public async Task<IReadOnlyList<DailySymbolBreakdown>> GetDailySymbolBreakdownAsync(DateTime date, CancellationToken cancellationToken)
    {
        var startUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var endUtc = startUtc.AddDays(1);

        const string sql = """
            SELECT
                symbol,
                COUNT(*) FILTER (WHERE side = 'Buy')              AS buy_count,
                COUNT(*) FILTER (WHERE side = 'Sell')             AS sell_count,
                COALESCE(SUM(filled_qty) FILTER (WHERE side = 'Buy'), 0)  AS buy_qty,
                COALESCE(SUM(filled_qty) FILTER (WHERE side = 'Sell'), 0) AS sell_qty,
                AVG(filled_price) FILTER (WHERE side = 'Buy')     AS avg_buy_price,
                AVG(filled_price) FILTER (WHERE side = 'Sell')    AS avg_sell_price,
                COALESCE(SUM(realized_pnl), 0)                    AS realized_pnl,
                COUNT(*) FILTER (WHERE realized_pnl > 0)          AS win_count,
                COUNT(*) FILTER (WHERE realized_pnl < 0)          AS loss_count,
                MAX(time)                                          AS last_trade_time
            FROM public.orders
            WHERE time >= @Start AND time < @End AND success = true
            GROUP BY symbol
            ORDER BY ABS(SUM(COALESCE(realized_pnl, 0))) DESC
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { Start = startUtc, End = endUtc }, cancellationToken: cancellationToken));

            return rows.Select(r => new DailySymbolBreakdown(
                Symbol: (string)r.symbol,
                BuyCount: (int)(long)r.buy_count,
                SellCount: (int)(long)r.sell_count,
                BuyQty: (decimal)r.buy_qty,
                SellQty: (decimal)r.sell_qty,
                AvgBuyPrice: r.avg_buy_price is null ? (decimal?)null : decimal.Round((decimal)r.avg_buy_price, 4),
                AvgSellPrice: r.avg_sell_price is null ? (decimal?)null : decimal.Round((decimal)r.avg_sell_price, 4),
                RealizedPnL: decimal.Round((decimal)r.realized_pnl, 4),
                WinCount: (int)(long)r.win_count,
                LossCount: (int)(long)r.loss_count,
                LastTradeTime: (DateTime?)r.last_trade_time
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch symbol breakdown for {Date}", date.Date);
            return [];
        }
    }

    public async Task<IReadOnlyList<TradeTimeRecord>> GetDailyTimeAnalyticsAsync(DateTime date, CancellationToken cancellationToken)
    {
        var startUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var endUtc = startUtc.AddDays(1);

        const string sql = """
            SELECT
                id           AS order_id,
                symbol,
                side,
                realized_pnl,
                time         AS open_time,
                exit_time    AS close_time,
                CASE WHEN exit_time IS NOT NULL
                     THEN EXTRACT(EPOCH FROM (exit_time - time)) / 60.0
                     ELSE NULL END AS holding_minutes
            FROM public.orders
            WHERE time >= @Start AND time < @End AND success = true
            ORDER BY time ASC
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { Start = startUtc, End = endUtc }, cancellationToken: cancellationToken));

            return rows.Select(r => new TradeTimeRecord(
                OrderId: (Guid)r.order_id,
                Symbol: (string)r.symbol,
                Side: (string)r.side,
                RealizedPnL: (decimal?)r.realized_pnl,
                OpenTime: (DateTime)r.open_time,
                CloseTime: (DateTime?)r.close_time,
                HoldingMinutes: (double?)r.holding_minutes
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch time analytics for {Date}", date.Date);
            return [];
        }
    }

    public async Task<IReadOnlyList<HourlyTradeBucket>> GetHourlyBucketsAsync(DateTime date, CancellationToken cancellationToken)
    {
        var startUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var endUtc = startUtc.AddDays(1);

        const string sql = """
            SELECT
                EXTRACT(HOUR FROM time)::int                       AS hour,
                COUNT(*) FILTER (WHERE side = 'Buy')               AS buy_count,
                COUNT(*) FILTER (WHERE side = 'Sell')              AS sell_count
            FROM public.orders
            WHERE time >= @Start AND time < @End AND success = true
            GROUP BY EXTRACT(HOUR FROM time)
            ORDER BY hour
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<dynamic>(
                new CommandDefinition(sql, new { Start = startUtc, End = endUtc }, cancellationToken: cancellationToken));

            return rows.Select(r => new HourlyTradeBucket(
                Hour: (int)r.hour,
                BuyCount: (int)(long)r.buy_count,
                SellCount: (int)(long)r.sell_count
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch hourly buckets for {Date}", date.Date);
            return [];
        }
    }

    // ── 4-Hour Session Report Queries ────────────────────────────────────────

    /// <summary>Returns all 6 session rows for the given date, with zeros for empty sessions.</summary>
    public async Task<IReadOnlyList<SessionReportRow>> GetSessionDailyReportAsync(
        DateTime date,
        CancellationToken cancellationToken)
    {
        var startUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var endUtc = startUtc.AddDays(1);
        var dateStr = startUtc.ToString("yyyyMMdd");

        var sql = $"""
            WITH date_sessions AS (
                SELECT
                    @DateStr || '-S' || gs::text                                          AS expected_session_id,
                    gs                                                                     AS session_num,
                    (@Start::timestamptz + ((gs - 1) * INTERVAL '8 hours'))               AS session_start,
                    (@Start::timestamptz + (gs       * INTERVAL '8 hours'))               AS session_end
                FROM generate_series(1, 3) gs
            ),
            order_stats AS (
                SELECT
                    (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int                         AS session_num,
                    COUNT(*) FILTER (WHERE success = true)                                 AS total_orders,
                    COUNT(*) FILTER (WHERE side = 'Buy'  AND success = true)               AS buy_count,
                    COUNT(*) FILTER (WHERE side = 'Sell' AND success = true)               AS sell_count,
                    COUNT(*) FILTER (WHERE success = false)                                AS rejected_count,
                    COUNT(*) FILTER (WHERE status = 'OPEN')                                AS open_count,
                    COUNT(*) FILTER (WHERE realized_pnl > 0)                               AS win_trades,
                    COUNT(*) FILTER (WHERE realized_pnl < 0)                               AS loss_trades,
                    COALESCE(SUM(realized_pnl), 0)                                         AS realized_pnl,
                    COUNT(DISTINCT symbol) FILTER (WHERE success = true)                   AS distinct_symbols,
                    STRING_AGG(DISTINCT symbol, ', ') FILTER (WHERE success = true)        AS symbols_csv
                FROM public.orders
                WHERE time >= @Start AND time < @End
                GROUP BY (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int
            )
            SELECT
                ds.expected_session_id                    AS session_id,
                ds.session_num,
                ds.session_start                          AS session_start_utc,
                ds.session_end                            AS session_end_utc,
                COALESCE(os.total_orders,     0)          AS total_orders,
                COALESCE(os.buy_count,        0)          AS buy_count,
                COALESCE(os.sell_count,       0)          AS sell_count,
                COALESCE(os.rejected_count,   0)          AS rejected_count,
                COALESCE(os.win_trades,       0)          AS win_trades,
                COALESCE(os.loss_trades,      0)          AS loss_trades,
                COALESCE(os.realized_pnl,     0)          AS realized_pnl,
                COALESCE(os.distinct_symbols, 0)          AS distinct_symbols,
                COALESCE(os.symbols_csv,      '')         AS symbols_csv,
                (COALESCE(os.open_count, 0) = 0)          AS is_flat_at_close
            FROM date_sessions ds
            LEFT JOIN order_stats os ON ds.session_num = os.session_num
            ORDER BY ds.session_num;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(
                sql,
                new { DateStr = dateStr, Start = startUtc, End = endUtc },
                cancellationToken: cancellationToken));

            return rows.Select(r => new SessionReportRow(
                SessionId: (string)r.session_id,
                SessionNumber: (int)r.session_num,
                SessionStartUtc: (DateTime)r.session_start_utc,
                SessionEndUtc: (DateTime)r.session_end_utc,
                TotalOrders: (int)(long)r.total_orders,
                BuyCount: (int)(long)r.buy_count,
                SellCount: (int)(long)r.sell_count,
                RejectedCount: (int)(long)r.rejected_count,
                WinTrades: (int)(long)r.win_trades,
                LossTrades: (int)(long)r.loss_trades,
                RealizedPnL: decimal.Round((decimal)r.realized_pnl, 4),
                DistinctSymbols: (int)(long)r.distinct_symbols,
                SymbolsCsv: (string)r.symbols_csv,
                IsFlatAtClose: (bool)r.is_flat_at_close
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch session daily report for {Date}", date.Date);
            return [];
        }
    }

    /// <summary>Returns session rows across a date range (multiple days × 3 sessions each).</summary>
    public async Task<IReadOnlyList<SessionReportRow>> GetSessionRangeReportAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var startUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(to.Date, DateTimeKind.Utc).AddDays(1);

        var sql = $"""
            SELECT
                TO_CHAR(time, 'YYYYMMDD') || '-S' || (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int  AS session_id,
                (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int                                        AS session_num,
                (DATE_TRUNC('day', time) + ((FLOOR(EXTRACT(HOUR FROM time) / 8))     * INTERVAL '8 hours'))
                                                                                                      AS session_start_utc,
                (DATE_TRUNC('day', time) + ((FLOOR(EXTRACT(HOUR FROM time) / 8) + 1) * INTERVAL '8 hours'))
                                                                                                      AS session_end_utc,
                COUNT(*) FILTER (WHERE success = true)                                                AS total_orders,
                COUNT(*) FILTER (WHERE side = 'Buy'  AND success = true)                              AS buy_count,
                COUNT(*) FILTER (WHERE side = 'Sell' AND success = true)                              AS sell_count,
                COUNT(*) FILTER (WHERE success = false)                                               AS rejected_count,
                COUNT(*) FILTER (WHERE realized_pnl > 0)                                              AS win_trades,
                COUNT(*) FILTER (WHERE realized_pnl < 0)                                              AS loss_trades,
                COALESCE(SUM(realized_pnl), 0)                                                        AS realized_pnl,
                COUNT(DISTINCT symbol) FILTER (WHERE success = true)                                  AS distinct_symbols,
                STRING_AGG(DISTINCT symbol, ', ') FILTER (WHERE success = true)                       AS symbols_csv,
                (COUNT(*) FILTER (WHERE status = 'OPEN') = 0)                                         AS is_flat_at_close
            FROM public.orders
            WHERE time >= @Start AND time < @End
            GROUP BY
                TO_CHAR(time, 'YYYYMMDD') || '-S' || (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int,
                (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int,
                (DATE_TRUNC('day', time) + ((FLOOR(EXTRACT(HOUR FROM time) / 8))     * INTERVAL '8 hours')),
                (DATE_TRUNC('day', time) + ((FLOOR(EXTRACT(HOUR FROM time) / 8) + 1) * INTERVAL '8 hours'))
            ORDER BY session_start_utc;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(
                sql,
                new { Start = startUtc, End = endUtc },
                cancellationToken: cancellationToken));

            return rows.Select(r => new SessionReportRow(
                SessionId: (string)r.session_id,
                SessionNumber: (int)r.session_num,
                SessionStartUtc: (DateTime)r.session_start_utc,
                SessionEndUtc: (DateTime)r.session_end_utc,
                TotalOrders: (int)(long)r.total_orders,
                BuyCount: (int)(long)r.buy_count,
                SellCount: (int)(long)r.sell_count,
                RejectedCount: (int)(long)r.rejected_count,
                WinTrades: (int)(long)r.win_trades,
                LossTrades: (int)(long)r.loss_trades,
                RealizedPnL: decimal.Round((decimal)r.realized_pnl, 4),
                DistinctSymbols: (int)(long)r.distinct_symbols,
                SymbolsCsv: (string)r.symbols_csv,
                IsFlatAtClose: (bool)r.is_flat_at_close
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch session range report from {From} to {To}", from.Date, to.Date);
            return [];
        }
    }

    /// <summary>Returns per-symbol breakdown for a specific session (e.g. "20240321-S3").</summary>
    public async Task<IReadOnlyList<SessionSymbolRow>> GetSessionSymbolsAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Parse sessionId: "20240321-S3" → date=2024-03-21, sessionNum=3
        if (!TryParseSessionId(sessionId, out var sessionStart, out var sessionEnd))
        {
            _logger.LogWarning("Invalid sessionId format: {SessionId}", sessionId);
            return [];
        }

        var sql = $"""
            SELECT
                @SessionId                                                                AS session_id,
                symbol,
                COUNT(*) FILTER (WHERE side = 'Buy'  AND success = true)                  AS buy_count,
                COUNT(*) FILTER (WHERE side = 'Sell' AND success = true)                  AS sell_count,
                COALESCE(SUM(filled_qty) FILTER (WHERE side = 'Buy'),  0)                 AS buy_qty,
                COALESCE(SUM(filled_qty) FILTER (WHERE side = 'Sell'), 0)                 AS sell_qty,
                AVG(filled_price) FILTER (WHERE side = 'Buy'  AND success = true)         AS avg_buy_price,
                AVG(filled_price) FILTER (WHERE side = 'Sell' AND success = true)         AS avg_sell_price,
                COALESCE(SUM(realized_pnl), 0)                                            AS realized_pnl,
                COUNT(*) FILTER (WHERE realized_pnl > 0)                                  AS win_trades,
                COUNT(*) FILTER (WHERE realized_pnl < 0)                                  AS loss_trades
            FROM public.orders
            WHERE time >= @Start AND time < @End
            GROUP BY symbol
            ORDER BY ABS(SUM(COALESCE(realized_pnl, 0))) DESC;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<dynamic>(new CommandDefinition(
                sql,
                new { SessionId = sessionId, Start = sessionStart, End = sessionEnd },
                cancellationToken: cancellationToken));

            return rows.Select(r => new SessionSymbolRow(
                SessionId: (string)r.session_id,
                Symbol: (string)r.symbol,
                BuyCount: (int)(long)r.buy_count,
                SellCount: (int)(long)r.sell_count,
                BuyQty: (decimal)r.buy_qty,
                SellQty: (decimal)r.sell_qty,
                AvgBuyPrice: r.avg_buy_price is null ? (decimal?)null : decimal.Round((decimal)r.avg_buy_price, 4),
                AvgSellPrice: r.avg_sell_price is null ? (decimal?)null : decimal.Round((decimal)r.avg_sell_price, 4),
                RealizedPnL: decimal.Round((decimal)r.realized_pnl, 4),
                WinTrades: (int)(long)r.win_trades,
                LossTrades: (int)(long)r.loss_trades
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch session symbol breakdown for {SessionId}", sessionId);
            return [];
        }
    }

    /// <summary>Returns per-session PnL with cumulative sum for an equity curve chart.</summary>
    public async Task<IReadOnlyList<SessionEquityPoint>> GetSessionEquityCurveAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var startUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(to.Date, DateTimeKind.Utc).AddDays(1);

        var sql = $"""
            SELECT
                TO_CHAR(time, 'YYYYMMDD') || '-S' || (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int  AS session_id,
                (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int                                        AS session_num,
                MIN(time)                                                                             AS session_start_utc,
                COALESCE(SUM(realized_pnl), 0)                                                       AS session_pnl
            FROM public.orders
            WHERE time >= @Start AND time < @End
                            AND success = true
            GROUP BY
                TO_CHAR(time, 'YYYYMMDD') || '-S' || (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int,
                (FLOOR(EXTRACT(HOUR FROM time) / 8) + 1)::int
            ORDER BY session_start_utc;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = (await connection.QueryAsync<dynamic>(new CommandDefinition(
                sql,
                new { Start = startUtc, End = endUtc },
                cancellationToken: cancellationToken))).ToList();

            decimal cumulative = 0m;
            return rows.Select(r =>
            {
                decimal pnl = decimal.Round((decimal)r.session_pnl, 4);
                cumulative += pnl;
                return new SessionEquityPoint(
                    SessionId: (string)r.session_id,
                    SessionNumber: (int)r.session_num,
                    SessionStartUtc: (DateTime)r.session_start_utc,
                    SessionPnL: pnl,
                    CumulativePnL: decimal.Round(cumulative, 4));
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch session equity curve from {From} to {To}", from.Date, to.Date);
            return [];
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a sessionId like "20240321-S2" into UTC start/end boundaries.
    /// Sessions are 8-hour blocks: S1=00:00-08:00, S2=08:00-16:00, S3=16:00-24:00.
    /// </summary>
    private static bool TryParseSessionId(string sessionId, out DateTime start, out DateTime end)
    {
        start = default;
        end = default;

        // Format: yyyyMMdd-SN
        if (sessionId is not { Length: >= 10 })
            return false;

        var parts = sessionId.Split('-');
        if (parts.Length != 2 || parts[1].Length < 2 || parts[1][0] != 'S')
            return false;

        if (!DateTime.TryParseExact(parts[0], "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return false;

        if (!int.TryParse(parts[1].AsSpan(1), out var sessionNum) || sessionNum < 1 || sessionNum > 3)
            return false;

        var dayUtc = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        start = dayUtc.AddHours((sessionNum - 1) * 8);
        end = dayUtc.AddHours(sessionNum * 8);
        return true;
    }

    // ── Recovery support ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the net open position for every symbol that has unbalanced buy/sell fills
    /// since <paramref name="sinceUtc"/>. Used by StartupReconciliationService to rebuild
    /// the in-memory PositionTracker after a crash.
    /// </summary>
    public async Task<IReadOnlyList<RecoveredPosition>> GetOpenPositionNetAsync(
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                symbol                                                         AS Symbol,
                ROUND(SUM(CASE WHEN side = 'Buy'  THEN COALESCE(filled_qty, quantity) ELSE 0 END), 8)::numeric(20,8)
                                                                               AS BoughtQty,
                ROUND(SUM(CASE WHEN side = 'Sell' THEN COALESCE(filled_qty, quantity) ELSE 0 END), 8)::numeric(20,8)
                                                                               AS SoldQty,
                CASE
                    WHEN SUM(CASE WHEN side = 'Buy' THEN COALESCE(filled_qty, quantity) ELSE 0 END) = 0
                    THEN NULL
                    ELSE ROUND(
                        SUM(CASE WHEN side = 'Buy'
                                 THEN COALESCE(filled_price, price, 0) * COALESCE(filled_qty, quantity)
                                 ELSE 0 END)
                        / SUM(CASE WHEN side = 'Buy' THEN COALESCE(filled_qty, quantity) ELSE 0 END),
                        8)::numeric(20,8)
                END                                                            AS AvgBuyPrice,
                MAX(CASE WHEN side = 'Buy' THEN session_id END)                AS SessionId
            FROM public.orders
            WHERE success = true
              AND time >= @SinceUtc
            GROUP BY symbol
            HAVING
                SUM(CASE WHEN side = 'Buy'  THEN COALESCE(filled_qty, quantity) ELSE 0 END) >
                SUM(CASE WHEN side = 'Sell' THEN COALESCE(filled_qty, quantity) ELSE 0 END);
            """;

        const int maxAttempts = 4;
        const int retryDelayMs = 3000;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                var rows = await connection.QueryAsync<RecoveredPosition>(
                    new CommandDefinition(sql, new { SinceUtc = sinceUtc }, cancellationToken: cancellationToken));
                return rows.AsList();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                _logger.LogWarning(ex,
                    "Failed to query open position net for recovery (attempt {Attempt}/{Max}). Retrying in {Delay}ms...",
                    attempt, maxAttempts, retryDelayMs);
                await Task.Delay(retryDelayMs, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query open position net for recovery after {Max} attempts", maxAttempts);
                return [];
            }
        }

        return [];
    }

    public sealed record RecoveredPosition(
        string Symbol,
        decimal BoughtQty,
        decimal SoldQty,
        decimal? AvgBuyPrice,
        string? SessionId)
    {
        public decimal NetQty => BoughtQty - SoldQty;
    }

    public sealed record StateDriftLogInput(
        string DriftType,
        string? Symbol,
        decimal BinanceValue,
        decimal LocalValue,
        string Severity,
        string RecoveryAction,
        string RecoveryDetail,
        bool RecoveryAttempted,
        bool RecoverySuccess);

    public sealed record StateDriftLogRow(
        Guid Id,
        Guid ReconciliationId,
        DateTime ReconciliationUtc,
        string? Symbol,
        string DriftType,
        string Environment,
        decimal BinanceValue,
        decimal LocalValue,
        string RecoveryAction,
        string RecoveryDetail,
        string Severity,
        bool RecoveryAttempted,
        bool RecoverySuccess,
        DateTime CreatedAt);

    public async Task InsertStateDriftLogsAsync(
        Guid reconciliationId,
        DateTime reconciliationUtc,
        string environment,
        IReadOnlyList<StateDriftLogInput> drifts,
        CancellationToken cancellationToken)
    {
        if (drifts.Count == 0)
            return;

        const string sql = """
            INSERT INTO public.state_drift_logs (
                id,
                reconciliation_id,
                reconciliation_utc,
                symbol,
                drift_type,
                environment,
                binance_value,
                local_value,
                recovery_action,
                recovery_detail,
                severity,
                recovery_attempted,
                recovery_success,
                created_at
            ) VALUES (
                @Id,
                @ReconciliationId,
                @ReconciliationUtc,
                @Symbol,
                @DriftType,
                @Environment,
                @BinanceValue,
                @LocalValue,
                @RecoveryAction,
                @RecoveryDetail,
                @Severity,
                @RecoveryAttempted,
                @RecoverySuccess,
                @CreatedAt
            );
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (var drift in drifts)
            {
                var parameters = new
                {
                    Id = Guid.NewGuid(),
                    ReconciliationId = reconciliationId,
                    ReconciliationUtc = reconciliationUtc,
                    drift.Symbol,
                    drift.DriftType,
                    Environment = environment,
                    drift.BinanceValue,
                    drift.LocalValue,
                    drift.RecoveryAction,
                    drift.RecoveryDetail,
                    drift.Severity,
                    drift.RecoveryAttempted,
                    drift.RecoverySuccess,
                    CreatedAt = DateTime.UtcNow
                };

                await connection.ExecuteAsync(new CommandDefinition(
                    sql,
                    parameters,
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            _logger.LogWarning(
                "state_drift_logs table does not exist yet; skipping drift persistence for reconciliation {ReconciliationId}",
                reconciliationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist state drift logs for reconciliation {ReconciliationId}",
                reconciliationId);
        }
    }

    public async Task<IReadOnlyList<StateDriftLogRow>> GetLatestStateDriftLogsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            WITH latest_cycle AS (
                SELECT reconciliation_id
                FROM public.state_drift_logs
                ORDER BY reconciliation_utc DESC, created_at DESC
                LIMIT 1
            )
            SELECT
                id                  AS "Id",
                reconciliation_id   AS "ReconciliationId",
                reconciliation_utc  AS "ReconciliationUtc",
                symbol              AS "Symbol",
                drift_type          AS "DriftType",
                environment         AS "Environment",
                binance_value       AS "BinanceValue",
                local_value         AS "LocalValue",
                recovery_action     AS "RecoveryAction",
                recovery_detail     AS "RecoveryDetail",
                severity            AS "Severity",
                recovery_attempted  AS "RecoveryAttempted",
                recovery_success    AS "RecoverySuccess",
                created_at          AS "CreatedAt"
            FROM public.state_drift_logs
            WHERE reconciliation_id = (SELECT reconciliation_id FROM latest_cycle)
            ORDER BY created_at ASC;
            """;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var rows = await connection.QueryAsync<StateDriftLogRow>(
                new CommandDefinition(sql, cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            _logger.LogWarning("state_drift_logs table does not exist yet; returning empty drift snapshot");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch latest reconciliation drift logs");
            return [];
        }
    }

    // ── Budget / Capital Tracking ─────────────────────────────────────────────

    public async Task<decimal> GetSessionRealizedPnLAsync(string sessionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(SUM(realized_pnl), 0)
            FROM public.orders
            WHERE session_id = @SessionId
              AND status = 'CLOSED'
              AND success = true;
            """;
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return await connection.QuerySingleAsync<decimal>(
                new CommandDefinition(sql, new { SessionId = sessionId }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get realized PnL for session {SessionId}", sessionId);
            return 0m;
        }
    }
}
