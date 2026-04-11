using Analyzer.Worker.Configuration;
using CryptoTrading.Shared.DTOs;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace Analyzer.Worker.Analysis;

/// <summary>
/// Computes RSI, EMA(9/21), Bollinger Bands, ADX, ATR, volume metrics,
/// BB squeeze, and market regime for each symbol, and derives the final <see cref="TradeSignal"/>.
/// </summary>
public sealed class IndicatorEngine
{
    private readonly PriceBuffer _buffer;
    private readonly AnalyzerSettings _settings;
    private readonly ILogger<IndicatorEngine> _logger;

    // Need enough candles for the slowest indicator (ADX needs ~2×period; keep generous buffer)
    private int MinimumCandles => Math.Max(
        Math.Max(_settings.RsiPeriod + 1, Math.Max(_settings.EmaLongPeriod, _settings.BbPeriod)),
        _settings.AdxPeriod * 2 + 1) + 5;

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
    /// Computes all indicators for <paramref name="symbol"/> and returns a <see cref="TradeSignal"/>,
    /// or <c>null</c> if there is not enough data yet.
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

            // ── Core indicators ─────────────────────────────────────────────
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

            // ── Phase 1.1: ADX trend strength ────────────────────────────────
            var adxRaw = quoteList.GetAdx(_settings.AdxPeriod).LastOrDefault()?.Adx;
            var adx = adxRaw.HasValue ? Math.Round((decimal)adxRaw.Value, 2) : 0m;

            // ── Phase 1.2: Volume confirmation ───────────────────────────────
            var (volumeRatio, volumeZscore) = ComputeVolumeMetrics(quoteList);
            var priceChange = latestQuote.Open > 0
                ? Math.Abs((latestQuote.Close - latestQuote.Open) / latestQuote.Open)
                : 0m;

            // ── Phase 2.1: ATR for adaptive SL ──────────────────────────────
            var atrRaw = quoteList.GetAtr(_settings.AtrPeriod).LastOrDefault()?.Atr;
            var atr14 = atrRaw.HasValue ? Math.Round((decimal)atrRaw.Value, 8) : 0m;

            // ── Phase 3.1: Market regime ─────────────────────────────────────
            var regime = DetectRegime(quoteList, adx, (decimal)ema9.Value, (decimal)ema21.Value,
                (decimal)bb.UpperBand.Value, (decimal)bb.LowerBand.Value, (decimal)bb.Sma.Value,
                atr14);

            // ── Phase 4.1: BB squeeze ────────────────────────────────────────
            var bbSqueeze = DetectBbSqueeze(quoteList,
                (decimal)bb.UpperBand.Value, (decimal)bb.LowerBand.Value);

            // ── Signal strength ──────────────────────────────────────────────
            var currentPrice = latestQuote.Close;
            var rsiDecimal = (decimal)rsi.Value;
            var baseStrength = EvaluateBaseStrength(
                rsiDecimal,
                (decimal)ema9.Value,
                (decimal)ema21.Value,
                currentPrice,
                (decimal)bb.UpperBand.Value,
                (decimal)bb.LowerBand.Value);

            var isSell = ema9.Value < ema21.Value;
            var (strength, volumeFlag) = ApplySignalFilters(
                baseStrength, adx, volumeRatio, volumeZscore, priceChange,
                isSell, rsiDecimal, bbSqueeze);

            if (strength != baseStrength || volumeFlag != null)
            {
                _logger.LogDebug(
                    "{Symbol} strength {Before}→{After} (ADX={Adx:F1} Vol={Vol:F2} Z={Z:F1}{Sq}{Flag})",
                    symbol, baseStrength, strength, adx, volumeRatio, volumeZscore,
                    bbSqueeze ? " SQUEEZE" : string.Empty,
                    volumeFlag != null ? $" {volumeFlag}" : string.Empty);
            }

            return new TradeSignal
            {
                Symbol = symbol,
                Rsi = Math.Round(rsiDecimal, 2),
                Ema9 = Math.Round((decimal)ema9.Value, 8),
                Ema21 = Math.Round((decimal)ema21.Value, 8),
                BbUpper = Math.Round((decimal)bb.UpperBand.Value, 8),
                BbMiddle = Math.Round((decimal)bb.Sma.Value, 8),
                BbLower = Math.Round((decimal)bb.LowerBand.Value, 8),
                Strength = strength,
                Timestamp = latestQuote.Date,
                Adx = adx,
                VolumeRatio = Math.Round(volumeRatio, 3),
                VolumeFlag = volumeFlag,
                Atr14 = atr14,
                Regime = regime,
                BbSqueeze = bbSqueeze
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing indicators for {Symbol}", symbol);
            return null;
        }
    }

    // ── Base strength ───────────────────────────────────────────────────────

    private SignalStrength EvaluateBaseStrength(
        decimal rsi, decimal ema9, decimal ema21,
        decimal price, decimal bbUpper, decimal bbLower)
    {
        bool bullish = ema9 >= ema21;
        int confirmations = 0;

        if (bullish)
        {
            if (rsi < _settings.RsiOversoldThreshold) confirmations++;
            if (price <= bbLower * 1.005m) confirmations++;
        }
        else
        {
            if (rsi > _settings.RsiOverboughtThreshold) confirmations++;
            if (price >= bbUpper * 0.995m) confirmations++;
        }

        return confirmations switch
        {
            2 => SignalStrength.Strong,
            1 => SignalStrength.Moderate,
            _ => SignalStrength.Weak
        };
    }

    // ── Signal filters (Phase 1, 2.4, 4.1) ──────────────────────────────────

    private (SignalStrength Strength, string? VolumeFlag) ApplySignalFilters(
        SignalStrength baseStrength,
        decimal adx,
        decimal volumeRatio,
        decimal volumeZscore,
        decimal priceChange,
        bool isSell,
        decimal rsi,
        bool bbSqueeze)
    {
        var strength = baseStrength;
        string? volumeFlag = null;

        // Phase 1.1: ADX trend filter
        if (adx > 0)
        {
            if (adx < _settings.AdxTrendThreshold)
                strength = SignalStrength.Weak;
            else if (adx > _settings.AdxStrongThreshold && strength == SignalStrength.Weak)
                strength = SignalStrength.Moderate;
        }

        // Phase 1.2: Volume confirmation
        if (volumeRatio < _settings.VolumeConfirmationRatio)
            strength = SignalStrength.Weak;

        if (volumeZscore > _settings.VolumeAnomalyZscoreThreshold && priceChange < 0.005m)
        {
            strength = SignalStrength.Weak;
            volumeFlag = "VOLUME_ANOMALY";
        }

        // Phase 2.4: Sell-side caution — avoid weak exits when momentum is still too strong.
        if (isSell)
        {
            if (rsi >= _settings.SellRsiThreshold)
                strength = SignalStrength.Weak;
        }

        // Phase 4.1: BB squeeze — enter only after confirmed breakout; pre-squeeze = no entry
        if (bbSqueeze)
            strength = SignalStrength.Weak;

        return (strength, volumeFlag);
    }

    // ── Volume metrics ───────────────────────────────────────────────────────

    private (decimal Ratio, decimal Zscore) ComputeVolumeMetrics(List<Quote> quotes)
    {
        if (quotes.Count < _settings.VolumeAveragePeriod)
            return (1m, 0m);

        var recent = quotes.TakeLast(_settings.VolumeAveragePeriod).ToList();
        var avgVolume = recent.Average(q => q.Volume);

        if (avgVolume == 0) return (1m, 0m);

        var currentVolume = quotes[^1].Volume;
        var ratio = currentVolume / avgVolume;

        var variance = recent.Average(q => Math.Pow((double)(q.Volume - avgVolume), 2));
        var stdDev = (decimal)Math.Sqrt(variance);
        var zscore = stdDev > 0 ? (currentVolume - avgVolume) / stdDev : 0m;

        return (ratio, zscore);
    }

    // ── Phase 3.1: Market regime (rule-based; can be upgraded to HMM in Phase 3) ──

    private MarketRegime DetectRegime(
        List<Quote> quotes,
        decimal adx,
        decimal ema9,
        decimal ema21,
        decimal bbUpper,
        decimal bbLower,
        decimal bbMiddle,
        decimal atr)
    {
        var bbWidth = bbMiddle > 0 ? (bbUpper - bbLower) / bbMiddle : 0m;

        // Compute historical ATR mean (last 30 candles that have valid ATR)
        var atrHistory = quotes.GetAtr(_settings.AtrPeriod)
            .Where(r => r.Atr.HasValue)
            .TakeLast(30)
            .Select(r => (decimal)r.Atr!.Value)
            .ToList();
        var atrHistMean = atrHistory.Count > 0 ? atrHistory.Average() : atr;

        // Compute historical BB width mean (up to last 100 candles)
        var bbHistory = quotes.GetBollingerBands(_settings.BbPeriod, _settings.BbStdDev)
            .Where(r => r.UpperBand.HasValue && r.LowerBand.HasValue && r.Sma.HasValue && r.Sma > 0)
            .TakeLast(100)
            .Select(r => ((decimal)r.UpperBand!.Value - (decimal)r.LowerBand!.Value) / (decimal)r.Sma!.Value)
            .ToList();
        var bbWidthHistMean = bbHistory.Count > 0 ? bbHistory.Average() : bbWidth;

        // HighVol: ATR significantly above mean AND BB width expanded beyond baseline
        if (atr > atrHistMean * _settings.RegimeHighVolAtrMultiplier
            && bbWidth > bbWidthHistMean * _settings.RegimeBbWidthExpansion)
            return MarketRegime.HighVolatility;

        // Trending: ADX indicates trend direction
        if (adx >= _settings.AdxTrendThreshold)
            return ema9 >= ema21 ? MarketRegime.TrendingUp : MarketRegime.TrendingDown;

        // Ranging / squeeze
        return MarketRegime.Ranging;
    }

    // ── Phase 4.1: BB squeeze ─────────────────────────────────────────────────

    private bool DetectBbSqueeze(List<Quote> quotes, decimal bbUpper, decimal bbLower)
    {
        if (quotes.Count < _settings.BbSqueezeBaselinePeriod)
            return false;

        var bbHistory = quotes.GetBollingerBands(_settings.BbPeriod, _settings.BbStdDev)
            .Where(r => r.UpperBand.HasValue && r.LowerBand.HasValue)
            .TakeLast(_settings.BbSqueezeBaselinePeriod)
            .Select(r => (decimal)r.UpperBand!.Value - (decimal)r.LowerBand!.Value)
            .ToList();

        if (bbHistory.Count < _settings.BbSqueezeBaselinePeriod / 2)
            return false;

        var avgWidth = bbHistory.Average();
        var currentWidth = bbUpper - bbLower;

        return avgWidth > 0 && currentWidth < avgWidth * _settings.BbSqueezeMultiplier;
    }
}
