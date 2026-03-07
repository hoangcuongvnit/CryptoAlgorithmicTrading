namespace CryptoAlgorithmicTrading.Gateway.API.Dashboard;

public interface IDashboardQueryService
{
    Task<OverviewResponse> GetOverviewAsync(
        DateTime startUtc,
        DateTime endUtc,
        string[] symbols,
        string interval,
        CancellationToken cancellationToken);

    Task<CandlesResponse> GetCandlesAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        string interval,
        string[] comparisonSymbols,
        CancellationToken cancellationToken);

    Task<QualityResponse> GetQualityAsync(
        DateTime startUtc,
        DateTime endUtc,
        string[] symbols,
        string interval,
        CancellationToken cancellationToken);

    Task<PagedResponse<GapRow>> GetGapsAsync(
        DateTime startUtc,
        DateTime endUtc,
        string[] symbols,
        string interval,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);

    Task<SchemaResponse> GetSchemaAsync(CancellationToken cancellationToken);

    Task<WorkbenchResponse> RunWorkbenchTemplateAsync(
        string templateId,
        DateTime startUtc,
        DateTime endUtc,
        string[] symbols,
        string interval,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken);
}