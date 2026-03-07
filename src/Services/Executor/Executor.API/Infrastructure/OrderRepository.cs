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
                strategy, is_paper, success, error_msg)
            VALUES (
                @Id, @Time, @Symbol, @Side, @OrderType, @Quantity, @Price,
                @FilledPrice, @FilledQty, @StopLoss, @TakeProfit,
                @Strategy, @IsPaper, @Success, @ErrorMessage);
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
            ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage) ? null : result.ErrorMessage
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
}
