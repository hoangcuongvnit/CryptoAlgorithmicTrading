using Dapper;
using Npgsql;
using CryptoTrading.Shared.DTOs;

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
                strategy, is_paper, success, error_msg, status)
            VALUES (
                @Id, @Time, @Symbol, @Side, @OrderType, @Quantity, @Price,
                @FilledPrice, @FilledQty, @StopLoss, @TakeProfit,
                @Strategy, @IsPaper, @Success, @ErrorMessage, @Status);
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
            Status = result.Success ? "OPEN" : "FAILED"
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
}
