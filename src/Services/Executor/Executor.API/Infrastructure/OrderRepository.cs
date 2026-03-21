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
                strategy, is_paper, success, error_msg, status,
                session_id, session_phase, is_reduce_only,
                forced_liquidation, liquidation_reason)
            VALUES (
                @Id, @Time, @Symbol, @Side, @OrderType, @Quantity, @Price,
                @FilledPrice, @FilledQty, @StopLoss, @TakeProfit,
                @Strategy, @IsPaper, @Success, @ErrorMessage, @Status,
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
            IsPaper = result.IsPaperTrade,
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
                   is_paper     AS IsPaperTrade,
                   success      AS Success,
                   error_msg    AS ErrorMessage,
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
        bool IsPaperTrade,
        bool Success,
        string? ErrorMessage,
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

    // ── Daily Report Queries ─────────────────────────────────────────────────

    public async Task<DailyReportSummary> GetDailyReportAsync(DateTime date, CancellationToken cancellationToken)
    {
        var startUtc = date.Date.ToUniversalTime();
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
        var startUtc = date.Date.ToUniversalTime();
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
        var startUtc = date.Date.ToUniversalTime();
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
        var startUtc = date.Date.ToUniversalTime();
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
}
