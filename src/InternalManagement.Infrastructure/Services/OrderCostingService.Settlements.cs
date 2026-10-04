using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class OrderCostingService
{
    public async Task<IReadOnlyList<SalesSettlementDto>> GetSettlementsAsync(Guid orderId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        return await _db.SalesSettlements
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.SalesOrderId == orderId)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => ToSettlementDto(x))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SalesDocumentDto>> GetDocumentsAsync(Guid orderId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var documents = await _db.SalesDocuments
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.SalesOrderId == orderId)
            .OrderByDescending(x => x.IssuedAt)
            .ToListAsync(ct);
        return documents.Select(ToDocumentDto).ToList();
    }

    public async Task<Result<SalesSettlementDto>> RecordSettlementAsync(
        Guid orderId,
        RecordSalesSettlementRequest request,
        CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.Amount <= 0)
        {
            return Result<SalesSettlementDto>.Failure("Số tiền thu/hoàn phải lớn hơn 0.");
        }
        if (string.IsNullOrWhiteSpace(request.PaymentReference))
        {
            return Result<SalesSettlementDto>.Failure("Mã tham chiếu thanh toán là bắt buộc để chống tạo trùng.");
        }
        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Status))
        {
            return Result<SalesSettlementDto>.Failure("Loại hoặc trạng thái thanh toán không hợp lệ.");
        }
        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            return Result<SalesSettlementDto>.Failure("Loại tiền tệ là bắt buộc.");
        }
        var occurredAt = NormalizeUtc(request.OccurredAt);

        var order = await _db.SalesOrders.FirstOrDefaultAsync(x =>
            x.Id == orderId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        if (order is null)
        {
            return Result<SalesSettlementDto>.Failure("Không tìm thấy đơn hàng.");
        }
        if (order.Status == SalesOrderStatus.Cancelled)
        {
            return Result<SalesSettlementDto>.Failure("Không thể ghi nhận thu/hoàn tiền cho đơn đã hủy.");
        }

        SalesDocument? salesDocument = null;
        if (request.SalesDocumentId.HasValue)
        {
            salesDocument = await _db.SalesDocuments.FirstOrDefaultAsync(x =>
                x.Id == request.SalesDocumentId.Value && x.CompanyId == companyId && x.SalesOrderId == order.Id, ct);
            if (salesDocument is null)
            {
                return Result<SalesSettlementDto>.Failure("Chứng từ bán hàng không thuộc đơn hàng này.");
            }
        }

        Return? returnEntity = null;
        if (request.Kind == SalesSettlementKind.CustomerRefund)
        {
            if (!request.ReturnId.HasValue)
            {
                return Result<SalesSettlementDto>.Failure("Hoàn tiền phải tham chiếu một phiếu đổi trả đã được duyệt.");
            }
            returnEntity = await _db.Returns.FirstOrDefaultAsync(x =>
                x.Id == request.ReturnId.Value && x.CompanyId == companyId && x.SalesOrderId == order.Id, ct);
            if (returnEntity is null || !string.Equals(returnEntity.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return Result<SalesSettlementDto>.Failure("Chỉ được hoàn tiền cho phiếu đổi trả đã được duyệt.");
            }
        }
        else if (request.ReturnId.HasValue)
        {
            return Result<SalesSettlementDto>.Failure("Khoản thu khách hàng không được tham chiếu phiếu đổi trả.");
        }

        var paymentReference = request.PaymentReference.Trim();
        var settlement = await _db.SalesSettlements.FirstOrDefaultAsync(x =>
            x.CompanyId == companyId && x.PaymentReference == paymentReference, ct);
        if (settlement is not null && settlement.SalesOrderId != order.Id)
        {
            return Result<SalesSettlementDto>.Failure("Mã tham chiếu thanh toán đã thuộc một đơn hàng khác.");
        }
        if (settlement is not null && settlement.Status == SalesSettlementStatus.Confirmed)
        {
            var identical = settlement.Kind == request.Kind
                            && settlement.Status == request.Status
                            && settlement.Amount == request.Amount
                            && settlement.Currency == request.Currency.Trim().ToUpperInvariant()
                            && settlement.PaymentMethod == NormalizeOptional(request.PaymentMethod)
                            && settlement.SalesDocumentId == request.SalesDocumentId
                            && settlement.ReturnId == request.ReturnId
                            && settlement.OccurredAt == occurredAt;
            return identical
                ? Result<SalesSettlementDto>.Success(ToSettlementDto(settlement))
                : Result<SalesSettlementDto>.Failure("Giao dịch đã xác nhận không được sửa. Hãy lập giao dịch điều chỉnh hoặc hoàn tiền riêng.");
        }

        var excludedId = settlement?.Id;
        if (request.Kind == SalesSettlementKind.CustomerPayment)
        {
            var recorded = await _db.SalesSettlements
                .Where(x => x.CompanyId == companyId && x.SalesOrderId == order.Id
                    && x.Kind == SalesSettlementKind.CustomerPayment
                    && (x.Status == SalesSettlementStatus.Pending || x.Status == SalesSettlementStatus.Confirmed)
                    && (!excludedId.HasValue || x.Id != excludedId.Value))
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            if (recorded + request.Amount > order.TotalAmount)
            {
                return Result<SalesSettlementDto>.Failure("Tổng tiền thu vượt quá giá trị đơn hàng.");
            }
        }
        else
        {
            var recorded = await _db.SalesSettlements
                .Where(x => x.CompanyId == companyId && x.ReturnId == returnEntity!.Id
                    && x.Kind == SalesSettlementKind.CustomerRefund
                    && (x.Status == SalesSettlementStatus.Pending || x.Status == SalesSettlementStatus.Confirmed)
                    && (!excludedId.HasValue || x.Id != excludedId.Value))
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            if (recorded + request.Amount > returnEntity!.TotalRefundAmount)
            {
                return Result<SalesSettlementDto>.Failure("Tổng tiền hoàn vượt quá số tiền đã duyệt trên phiếu đổi trả.");
            }
        }

        if (settlement is null)
        {
            settlement = new SalesSettlement
            {
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                SalesOrderId = order.Id,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.Username ?? "system"
            };
            _db.SalesSettlements.Add(settlement);
        }

        settlement.SalesDocumentId = salesDocument?.Id;
        settlement.ReturnId = returnEntity?.Id;
        settlement.Kind = request.Kind;
        settlement.Status = request.Status;
        settlement.PaymentReference = paymentReference;
        settlement.Amount = request.Amount;
        settlement.Currency = request.Currency.Trim().ToUpperInvariant();
        settlement.PaymentMethod = NormalizeOptional(request.PaymentMethod);
        settlement.OccurredAt = occurredAt;
        settlement.UpdatedAt = DateTime.UtcNow;
        settlement.UpdatedBy = _currentUser.Username ?? "system";

        if (_documentRegistry is not null)
        {
            var document = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId,
                businessUnitId,
                BusinessDocumentType.FashionSettlement,
                nameof(SalesSettlement),
                settlement.Id,
                $"SET-{paymentReference}",
                settlement.Amount,
                settlement.OccurredAt,
                order.CustomerPartyId,
                settlement.Currency,
                ToDocumentStatus(settlement.Status),
                order.SourceSystem,
                paymentReference), ct);
            settlement.BusinessDocumentId = document.Id;
        }

        if (settlement.Status == SalesSettlementStatus.Confirmed && _financePostingService is not null)
        {
            var sourceBusinessDocumentId = settlement.Kind == SalesSettlementKind.CustomerRefund
                ? returnEntity!.BusinessDocumentId
                : salesDocument?.BusinessDocumentId ?? order.BusinessDocumentId;
            var transactionType = settlement.Kind == SalesSettlementKind.CustomerPayment
                ? TransactionType.Income
                : TransactionType.Expense;
            var description = transactionType == TransactionType.Income
                ? $"Thu đơn Fashion {order.OrderNumber} ({settlement.PaymentReference})"
                : $"Hoàn tiền đơn Fashion {order.OrderNumber} ({settlement.PaymentReference})";
            await _financePostingService.PostAsync(new FinancePostingRequest(
                companyId,
                businessUnitId,
                transactionType,
                settlement.Amount,
                settlement.OccurredAt,
                nameof(SalesSettlement),
                settlement.Id,
                description,
                sourceBusinessDocumentId), ct);
        }

        await _db.SaveChangesAsync(ct);
        return Result<SalesSettlementDto>.Success(ToSettlementDto(settlement));
    }

    public async Task<Result<SalesDocumentDto>> IssueDocumentAsync(Guid orderId, IssueSalesDocumentRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.DocumentType is not (SalesDocumentType.Invoice or SalesDocumentType.RetailReceipt))
        {
            return Result<SalesDocumentDto>.Failure("Loại chứng từ bán hàng không hợp lệ.");
        }

        var order = await _db.SalesOrders
            .Include(x => x.Items)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == orderId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        if (order == null)
        {
            return Result<SalesDocumentDto>.Failure("Không tìm thấy đơn hàng.");
        }
        if (order.Status == SalesOrderStatus.Cancelled)
        {
            return Result<SalesDocumentDto>.Failure("Không thể phát hành chứng từ cho đơn hàng đã hủy.");
        }
        if (await _db.SalesDocuments.AnyAsync(x => x.CompanyId == companyId && x.SalesOrderId == order.Id && x.DocumentType == request.DocumentType, ct))
        {
            return Result<SalesDocumentDto>.Failure("Đơn hàng đã có chứng từ cùng loại.");
        }

        var prefix = request.DocumentType == SalesDocumentType.Invoice ? "INV" : "POS";
        var document = new SalesDocument
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SalesOrderId = order.Id,
            CustomerPartyId = order.CustomerPartyId,
            DocumentType = request.DocumentType,
            DocumentNumber = $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Random.Shared.Next(100, 999)}",
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = order.ShippingAddress,
            GrossAmount = order.GrossAmount,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            TotalAmount = order.TotalAmount,
            IssuedAt = DateTime.UtcNow
        };

        var gross = order.Items.Sum(x => x.TotalPrice);
        var remainingTax = order.TaxAmount;
        for (var index = 0; index < order.Items.Count; index++)
        {
            var orderItem = order.Items.ElementAt(index);
            var lineTotal = orderItem.TotalPrice;
            var lineTax = index == order.Items.Count - 1 || gross <= 0
                ? remainingTax
                : decimal.Round(order.TaxAmount * lineTotal / gross, 2);
            remainingTax -= lineTax;
            var variant = orderItem.ProductVariant;
            document.Items.Add(new SalesDocumentItem
            {
                SalesDocumentId = document.Id,
                ProductVariantId = orderItem.ProductVariantId,
                SkuSnapshot = string.IsNullOrWhiteSpace(orderItem.SkuSnapshot) ? variant.Sku : orderItem.SkuSnapshot,
                ProductNameSnapshot = string.IsNullOrWhiteSpace(orderItem.ProductNameSnapshot) ? variant.Product.Name : orderItem.ProductNameSnapshot,
                ColorSnapshot = variant.Color,
                SizeSnapshot = variant.Size,
                Quantity = orderItem.Quantity,
                UnitPrice = orderItem.UnitPrice,
                TaxAmount = lineTax,
                LineTotal = lineTotal
            });
        }

        if (_documentRegistry is not null)
        {
            var businessDocument = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId,
                businessUnitId,
                BusinessDocumentType.FashionSalesDocument,
                nameof(SalesDocument),
                document.Id,
                document.DocumentNumber,
                document.TotalAmount,
                document.IssuedAt,
                document.CustomerPartyId,
                ExternalSourceSystem: order.SourceSystem,
                ExternalSourceId: order.SourceOrderId), ct);
            document.BusinessDocumentId = businessDocument.Id;
        }
        _db.SalesDocuments.Add(document);
        await _db.SaveChangesAsync(ct);
        return Result<SalesDocumentDto>.Success(ToDocumentDto(document));
    }

}
