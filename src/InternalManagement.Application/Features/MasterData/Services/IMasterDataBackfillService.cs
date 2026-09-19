namespace InternalManagement.Application.Features.MasterData.Services;

/// <summary>
/// Summary returned by the explicitly triggered, idempotent legacy-data migration.
/// It is intentionally separate from the EF schema migration so operational data
/// is never silently rewritten during application startup.
/// </summary>
public sealed record MasterDataBackfillResult(
    int PartiesLinked,
    int DocumentsLinked,
    int DocumentLinksCreated,
    int FinanceTransactionsPosted = 0);

public interface IMasterDataBackfillService
{
    Task<MasterDataBackfillResult> BackfillAsync(int batchSize, CancellationToken ct);
}
