using Npgsql;

var connectionString = "Host=localhost;Port=5433;Database=cryptotrading;Username=postgres;Password=postgres";
var sqlFile = Path.Combine(Directory.GetCurrentDirectory(), "..", "create-orders-table.sql");

var sql = await File.ReadAllTextAsync(sqlFile);

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

await using var cmd = new NpgsqlCommand(sql, connection);
await cmd.ExecuteNonQueryAsync();

Console.WriteLine("Orders table created successfully.");
