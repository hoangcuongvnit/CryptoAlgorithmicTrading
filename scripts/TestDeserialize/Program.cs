using System.Text.Json;
using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Json;

var json = """
{"symbol":"BTCUSDT","rsi":28.5,"ema9":65010.12,"ema21":64950.34,"bbUpper":65500,"bbMiddle":65000,"bbLower":64500,"strength":3,"timestamp":"2026-03-07T12:00:00Z"}
""";

Console.WriteLine($"JSON: {json}");

try
{
    var signal = JsonSerializer.Deserialize(json, TradingJsonContext.Default.TradeSignal);
    Console.WriteLine($"Deserialized successfully: {signal}");
}
catch (Exception ex)
{
    Console.WriteLine($"Deserialization failed: {ex.Message}");
}
