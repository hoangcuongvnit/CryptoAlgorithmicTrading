namespace Gateway.API.Dashboard;

public sealed record OverviewResponse(
    DateTime StartUtc,
    DateTime EndUtc,
    string Interval,
    IReadOnlyList<OverviewSymbolRow> Symbols,
    int TotalOpenGaps);

public sealed record OverviewSymbolRow(
    string Symbol,
    long RowCount,
    DateTime? LatestTimeUtc,
    double? FreshnessMinutes,
    double CoveragePercent,
    int DaysBelowExpected);

public sealed record CandlesResponse(
    string Symbol,
    string Interval,
    IReadOnlyList<CandlePoint> Candles,
    IReadOnlyList<SeriesPoint> Comparison);

public sealed record CandlePoint(
    DateTime Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

public sealed record SeriesPoint(
    DateTime Time,
    string Symbol,
    decimal Close);

public sealed record QualityResponse(
    DateTime StartUtc,
    DateTime EndUtc,
    string Interval,
    IReadOnlyList<DailyCoverageRow> DailyCoverage,
    IReadOnlyList<GapRow> Gaps,
    IReadOnlyList<HistogramBucket> GapDurationHistogram);

public sealed record DailyCoverageRow(
    DateOnly Day,
    string Symbol,
    int ExpectedCandles,
    int ActualCandles,
    int MissingCandles);

public sealed record GapRow(
    long Id,
    string Symbol,
    string Interval,
    DateTime GapStart,
    DateTime GapEnd,
    DateTime DetectedAt,
    DateTime? FilledAt,
    double DurationMinutes,
    double? FillLatencyMinutes);

public sealed record HistogramBucket(string Label, int Count);

public sealed record SchemaResponse(
    IReadOnlyList<TableInfo> Tables,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<ConstraintInfo> Constraints,
    IReadOnlyList<IndexInfo> Indexes);

public sealed record TableInfo(string SchemaName, string TableName, string TableType);

public sealed record ColumnInfo(
    string SchemaName,
    string TableName,
    string ColumnName,
    string DataType,
    bool IsNullable,
    int OrdinalPosition);

public sealed record ConstraintInfo(
    string TableName,
    string ConstraintName,
    string ConstraintType,
    string ColumnName);

public sealed record IndexInfo(
    string SchemaName,
    string TableName,
    string IndexName,
    string IndexDefinition);

public sealed record PagedResponse<T>(
    int PageNumber,
    int PageSize,
    int TotalRows,
    IReadOnlyList<T> Rows);

public sealed record OrderRow(
    Guid OrderId,
    string Symbol,
    string Side,
    string OrderType,
    decimal Quantity,
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    decimal? FilledPrice,
    decimal? FilledQty,
    bool? Success,
    bool IsPaperTrade,
    string? ErrorMessage,
    DateTime CreatedAt);

public sealed record WorkbenchResponse(
    string TemplateId,
    int PageNumber,
    int PageSize,
    int TotalRows,
    IReadOnlyList<string> Columns,
    IReadOnlyList<Dictionary<string, object?>> Rows);

public sealed record PriceSummary(
    decimal? OpenPrice,
    decimal? HighPrice,
    decimal? LowPrice,
    decimal? ClosePrice,
    long TotalTicks,
    DateTime? FirstTickUtc,
    DateTime? LastTickUtc);