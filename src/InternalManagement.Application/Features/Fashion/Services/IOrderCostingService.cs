using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;

namespace InternalManagement.Application.Features.Fashion.Services;

public interface IOrderCostingService
{
    Task<Result<SalesOrderCostDto>> CreateOrderAndSnapshotAsync(CreateSalesOrderRequest request, CancellationToken ct);
    Task<PaginatedResult<SalesOrderSummaryDto>> GetOrdersAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<SalesOrderFulfillmentDto?> GetFulfillmentAsync(Guid orderId, CancellationToken ct);
    Task<Result<SalesOrderSummaryDto>> UpdateOrderHeaderAsync(Guid orderId, UpdateSalesOrderHeaderRequest request, CancellationToken ct);
    Task<Result<SalesOrderSummaryDto>> CancelOrderAsync(Guid orderId, CancelSalesOrderRequest request, CancellationToken ct);
    Task<SalesOrderCostDto?> GetLatestSnapshotAsync(Guid orderId, CancellationToken ct);
    Task<Result<SalesOrderFulfillmentDto>> DeliverAsync(Guid orderId, FulfillSalesOrderRequest request, CancellationToken ct);
    Task<Result<ReturnDto>> CreateReturnAsync(CreateReturnRequest request, CancellationToken ct);
    Task<Result<ReturnDto>> InspectReturnAsync(Guid returnId, InspectReturnRequest request, CancellationToken ct);
    Task<Result<SalesSettlementDto>> RecordSettlementAsync(Guid orderId, RecordSalesSettlementRequest request, CancellationToken ct);
    Task<IReadOnlyList<SalesSettlementDto>> GetSettlementsAsync(Guid orderId, CancellationToken ct);
    Task<IReadOnlyList<SalesDocumentDto>> GetDocumentsAsync(Guid orderId, CancellationToken ct);
    Task<Result<SalesDocumentDto>> IssueDocumentAsync(Guid orderId, IssueSalesDocumentRequest request, CancellationToken ct);
    Task<Result<ImportSalesOrderResult>> ImportAsync(ImportSalesOrderRequest request, CancellationToken ct);
}
