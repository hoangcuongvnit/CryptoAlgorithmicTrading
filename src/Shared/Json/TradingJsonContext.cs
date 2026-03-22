using CryptoTrading.Shared.DTOs;
using CryptoTrading.Shared.Session;
using System.Text.Json.Serialization;

namespace CryptoTrading.Shared.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(PriceTick))]
[JsonSerializable(typeof(TradeSignal))]
[JsonSerializable(typeof(MarketRegime))]
[JsonSerializable(typeof(OrderRequest))]
[JsonSerializable(typeof(OrderResult))]
[JsonSerializable(typeof(SystemEvent))]
[JsonSerializable(typeof(RiskValidationResult))]
[JsonSerializable(typeof(SessionInfo))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class TradingJsonContext : JsonSerializerContext
{
}
