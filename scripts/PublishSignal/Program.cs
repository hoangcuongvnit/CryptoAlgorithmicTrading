using StackExchange.Redis;
using System.Text.Json;

var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var subscriber = redis.GetSubscriber();

var signal = new
{
    symbol = "BTCUSDT",
    rsi = 28.5,
    ema9 = 65010.12,
    ema21 = 64950.34,
    bbUpper = 65500.0,
    bbMiddle = 65000.0,
    bbLower = 64500.0,
    strength = 3,
    timestamp = DateTime.UtcNow.ToString("o")
};

var json = JsonSerializer.Serialize(signal, new JsonSerializerOptions 
{ 
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
});

Console.WriteLine($"Publishing: {json}");
var count = await subscriber.PublishAsync("signal:BTCUSDT", json);
Console.WriteLine($"Published to {count} subscribers");

await redis.CloseAsync();
