using RiskGuard.API.Services;

namespace RiskGuard.API.Infrastructure;

public interface IRedisPersistenceService
{
    Task SetCooldownAsync(string symbol, DateTime timestampUtc, CancellationToken ct);
    Task<Dictionary<string, DateTime>> LoadAllCooldownsAsync(CancellationToken ct);
    Task DeleteCooldownAsync(string symbol, CancellationToken ct);

    Task AddValidationAsync(ValidationRecord record, CancellationToken ct);
    Task<List<ValidationRecord>> LoadTodayValidationsAsync(CancellationToken ct);
    Task<(int Approved, int Rejected)> LoadTodayCountsAsync(CancellationToken ct);
    Task UpdateTodayCountsAsync(int approved, int rejected, CancellationToken ct);

    Task<bool> IsAvailableAsync(CancellationToken ct);
}