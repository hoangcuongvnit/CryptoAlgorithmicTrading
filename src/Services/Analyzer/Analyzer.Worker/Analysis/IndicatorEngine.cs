using Analyzer.Worker.Configuration;
using CryptoTrading.Shared.DTOs;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace Analyzer.Worker.Analysis;

/// <summary>
/// Computes RSI, EMA(9/21), and Bollinger Bands from the price buffer
/// and determines signal strength for each symbol.
/// </summary>
public sealed class IndicatorEngine
{
    private readonly PriceBuffer _buffer;
    private readonly AnalyzerSettings _settings;
    private readonly ILogger<IndicatorEngine> _logger;

    // Need at least max(RsiPeriod+1, EmaLongPeriod, BbPeriod) candles
    private int MinimumCandles => Math.Max(
        _settings.RsiPeriod + 1,
        Math.Max(_settings.EmaLongPeriod, _settings.BbPeriod)) + 5;

    public IndicatorEngine(
        PriceBuffer buffer,
        IOptions<AnalyzerSettings> settings,
        ILogger<IndicatorEngine> logger)
    {
        _buffer = buffer;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Computes indicators for <paramref name="symbol"/> and returns a <see cref="TradeSignal"/>,
    /// or <c>null</c> if there isn't enough data yet.
    /// </summary>
    public TradeSignal? TryComputeSignal(string symbol)
    {
        var quotes = _buffer.GetQuotes(symbol);
        if (quotes == null || quotes.Count < MinimumCandles)
            return null;

        try
        {
            var quoteList = quotes.ToList();
            var latestQuote = quoteList[^1];

            var rsiResults = quoteList.GetRsi(_settings.RsiPeriod);
            var rsi = rsiResults.LastOrDefault()?.Rsi;
            if (rsi == null) return null;

            var ema9Results = quoteList.GetEma(_settings.EmaShortPeriod);
            var ema21Results = quoteList.GetEma(_settings.EmaLongPeriod);
            var ema9 = ema9Results.LastOrDefault()?.Ema;
            var ema21 = ema21Results.LastOrDefault()?.Ema;
            if (ema9 == null || ema21 == null) return null;

            var bbResults = quoteList.GetBollingerBands(_settings.BbPeriod, _settings.BbStdDev);
            var bb = bbResults.LastOrDefault();
            if (bb?.UpperBand == null || bb.LowerBand == null || bb.Sma == null) return null;

            var currentPrice = latestQuote.Close;
            var strength = EvaluateStrength(
                (decimal)rsi.Value,
                (decimal)ema9.Value,
                (decimal)ema21.Value,
                currentPrice,
                (decimal)bb.UpperBand.Value,
                (decimal)bb.LowerBand.Value);

            return new TradeSignal
            {
                Symbol = symbol,
                Rsi = Math.Round((decimal)rsi.Value, 2),
                Ema9 = Math.Round((decimal)ema9.Value, 8),
                Ema21 = Math.Round((decimal)ema21.Value, 8),
                BbUpper = Math.Round((decimal)bb.UpperBand.Value, 8),
                BbMiddle = Math.Round((decimal)bb.Sma.Value, 8),
                BbLower = Math.Round((decimal)bb.LowerBand.Value, 8),
                Strength = strength,
                Timestamp = latestQuote.Date
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing indicators for {Symbol}", symbol);
            return null;
        }
    }

    private SignalStrength EvaluateStrength(
        decimal rsi, decimal ema9, decimal ema21,
        decimal price, decimal bbUpper, decimal bbLower)
    {
        bool bullish = ema9 >= ema21;

        int confirmations = 0;

        if (bullish)
        {
            // RSI oversold: bullish confirmation
            if (rsi < _settings.RsiOversoldThreshold) confirmations++;
            // Price at or below lower BB: bullish confirmation
            if (price <= bbLower * 1.005m) confirmations++;
        }
        else
        {
            // RSI overbought: bearish confirmation
            if (rsi > _settings.RsiOverboughtThreshold) confirmations++;
            // Price at or above upper BB: bearish confirmation
            if (price >= bbUpper * 0.995m) confirmations++;
        }

        return confirmations switch
        {
            2 => SignalStrength.Strong,
            1 => SignalStrength.Moderate,
            _ => SignalStrength.Weak
        };
    }
}
