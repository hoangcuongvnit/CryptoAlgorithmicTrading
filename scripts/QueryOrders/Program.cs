using Npgsql;

var connectionString = "Host=localhost;Port=5433;Database=cryptotrading;Username=postgres;Password=postgres";

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

await using var cmd = new NpgsqlCommand(
    "SELECT id, symbol, side, quantity, filled_price, success, time FROM orders ORDER BY time DESC LIMIT 3",
    connection);

await using var reader = await cmd.ExecuteReaderAsync();

Console.WriteLine($"{"OrderId",-36} | {"Symbol",-10} | {"Side",-4} | {"Qty",10} | {"FillPrice",10} | Success | Time");
Console.WriteLine(new string('-', 108));

while (await reader.ReadAsync())
{
    var orderId = reader.GetGuid(0);
    var symbol = reader.GetString(1);
    var side = reader.GetString(2);
    var qty = reader.GetDecimal(3);
    var fillPrice = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4);
    var success = reader.GetBoolean(6);
    var time = reader.GetDateTime(7);

    Console.WriteLine($"{orderId,-36} | {symbol,-10} | {side,-4} | {qty,10:F8} | {fillPrice,10:F2} | {success,7} | {time:yyyy-MM-dd HH:mm:ss}");
}
