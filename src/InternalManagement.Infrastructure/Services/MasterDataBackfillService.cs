using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.InternalData;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

/// <summary>
/// Explicit, repeat-safe upgrade path for records created before Party and
/// BusinessDocument existed. It is invoked by an administrator after the schema
/// migration—not automatically during startup—so a failed or long-running
/// backfill cannot block the business application.
/// </summary>
public sealed class MasterDataBackfillService(
    IApplicationDbContext db,
    IPartyResolver partyResolver,
    IBusinessDocumentRegistry documentRegistry,
    IFinancePostingService? financePostingService = null) : IMasterDataBackfillService
{
    public async Task<MasterDataBackfillResult> BackfillAsync(int batchSize, CancellationToken ct)
    {
        batchSize = Math.Clamp(batchSize, 1, 500);

        var partiesLinked = 0;
        partiesLinked += await BackfillEdTechCustomersAsync(batchSize, ct);
        partiesLinked += await BackfillInterviewCustomersAsync(batchSize, ct);
        partiesLinked += await BackfillSuppliersAsync(batchSize, ct);
        partiesLinked += await BackfillCscaStudentsAsync(batchSize, ct);
        partiesLinked += await BackfillInternalCustomersAsync(batchSize, ct);

        var documentsLinked = 0;
        documentsLinked += await BackfillPaymentsAsync(batchSize, ct);
        documentsLinked += await BackfillCscaEnrollmentDocumentsAsync(batchSize, ct);
        documentsLinked += await BackfillInterviewDocumentsAsync(batchSize, ct);
        documentsLinked += await BackfillPurchaseReceiptsAsync(batchSize, ct);
        documentsLinked += await BackfillSalesOrdersAsync(batchSize, ct);
        documentsLinked += await BackfillSalesDocumentsAsync(batchSize, ct);
        documentsLinked += await BackfillReturnsAsync(batchSize, ct);
        documentsLinked += await BackfillPayrollPeriodsAsync(batchSize, ct);
        var financeResult = await BackfillFinanceTransactionsAsync(batchSize, ct);
        documentsLinked += financeResult.DocumentsLinked;
        var financeTransactionsPosted = await BackfillConfirmedOperationalPostingsAsync(batchSize, ct);

        return new MasterDataBackfillResult(
            partiesLinked,
            documentsLinked,
            financeResult.DocumentLinksCreated + financeTransactionsPosted,
            financeTransactionsPosted);
    }

    private async Task<int> BackfillEdTechCustomersAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var customers = await db.EdTechCustomers
                .Where(x => x.PartyId == null)
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (customers.Count == 0) return count;

            foreach (var customer in customers)
            {
                var party = await partyResolver.ResolveAsync(new PartyResolutionRequest(
                    customer.CompanyId, customer.BusinessUnitId, PartyType.Individual, PartyRole.Customer,
                    customer.FullName, customer.Email, customer.PhoneNumber, customer.SourceSystem, customer.SourceId), ct);
                customer.PartyId = party.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillInterviewCustomersAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var customers = await db.InterviewCustomers
                .Where(x => x.PartyId == null)
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (customers.Count == 0) return count;

            foreach (var customer in customers)
            {
                var party = await partyResolver.ResolveAsync(new PartyResolutionRequest(
                    customer.CompanyId, customer.BusinessUnitId, PartyType.Individual, PartyRole.Customer,
                    customer.FullName, customer.Email, customer.Phone, customer.SourceSystem, customer.SourceId), ct);
                customer.PartyId = party.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillSuppliersAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var suppliers = await db.Suppliers
                .Where(x => x.PartyId == null)
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (suppliers.Count == 0) return count;

            foreach (var supplier in suppliers)
            {
                var party = await partyResolver.ResolveAsync(new PartyResolutionRequest(
                    supplier.CompanyId, supplier.BusinessUnitId, PartyType.Organization, PartyRole.Supplier,
                    supplier.Name, supplier.Email, supplier.Phone, "FASHION_SUPPLIER", supplier.Code), ct);
                supplier.PartyId = party.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillCscaStudentsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var students = await db.CscaClassStudents
                .Include(x => x.Class)
                .Where(x => x.PartyId == null)
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (students.Count == 0) return count;

            foreach (var student in students)
            {
                var party = await partyResolver.ResolveAsync(new PartyResolutionRequest(
                    student.Class.CompanyId, student.Class.BusinessUnitId, PartyType.Individual, PartyRole.Student,
                    student.StudentName, student.Email, student.PhoneNumber, "CSCA_CLASS_STUDENT", student.Id.ToString("N")), ct);
                student.PartyId = party.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillInternalCustomersAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var customers = await db.InternalCustomers
                .Where(x => x.PartyId == null)
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (customers.Count == 0) return count;

            foreach (var customer in customers)
            {
                var sourceId = string.IsNullOrWhiteSpace(customer.Code) ? customer.Id.ToString("N") : customer.Code;
                var party = await partyResolver.ResolveAsync(new PartyResolutionRequest(
                    customer.CompanyId, customer.BusinessUnitId, PartyType.Organization, PartyRole.Customer,
                    customer.Name, customer.Email, customer.Phone, $"INTERNAL_{customer.BusinessSegment}", sourceId), ct);
                customer.PartyId = party.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillPaymentsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var payments = await db.Payments.Include(x => x.Customer)
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.PaidAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (payments.Count == 0) return count;

            foreach (var payment in payments)
            {
                var document = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    payment.CompanyId, payment.BusinessUnitId, BusinessDocumentType.EdTechPayment, nameof(Payment),
                    payment.Id, $"PAY-{payment.Id:N}", payment.Amount, payment.PaidAt, payment.Customer.PartyId,
                    payment.Currency, ToDocumentStatus(payment.Status), payment.SourceSystem, payment.SourcePaymentId), ct);
                payment.BusinessDocumentId = document.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillInterviewDocumentsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var customers = await db.InterviewCustomers
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (customers.Count == 0) return count;

            foreach (var customer in customers)
            {
                var document = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    customer.CompanyId, customer.BusinessUnitId, BusinessDocumentType.InterviewService, nameof(InterviewCustomer),
                    customer.Id, $"INTERVIEW-{customer.Id:N}", customer.PaidAmount,
                    customer.UpdatedAt ?? customer.CreatedAt, customer.PartyId, Status: ToDocumentStatus(customer.Status),
                    ExternalSourceSystem: customer.SourceSystem, ExternalSourceId: customer.SourceId), ct);
                customer.BusinessDocumentId = document.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillCscaEnrollmentDocumentsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var students = await db.CscaClassStudents
                .Include(x => x.Class)
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (students.Count == 0) return count;

            foreach (var student in students)
            {
                var document = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    student.Class.CompanyId, student.Class.BusinessUnitId, BusinessDocumentType.CscaEnrollment,
                    nameof(CscaClassStudent), student.Id, $"CSCA-ENROLL-{student.Id:N}", student.PaidAmount,
                    student.UpdatedAt ?? student.JoinedAt, student.PartyId, Status: ToDocumentStatus(student.PaymentStatus),
                    ExternalSourceSystem: "CSCA_CLASS_STUDENT", ExternalSourceId: student.Id.ToString("N")), ct);
                student.BusinessDocumentId = document.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillPurchaseReceiptsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var receipts = await db.PurchaseReceipts.Include(x => x.Supplier)
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.ReceivedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (receipts.Count == 0) return count;

            foreach (var receipt in receipts)
            {
                var document = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    receipt.CompanyId, receipt.BusinessUnitId, BusinessDocumentType.FashionPurchaseReceipt, nameof(PurchaseReceipt),
                    receipt.Id, receipt.ReceiptNumber, receipt.TotalAmount, receipt.ReceivedAt, receipt.Supplier.PartyId,
                    ExternalSourceSystem: "FASHION_SUPPLIER", ExternalSourceId: receipt.Supplier.Code), ct);
                receipt.BusinessDocumentId = document.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillSalesOrdersAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var orders = await db.SalesOrders
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.OrderDate)
                .Take(batchSize)
                .ToListAsync(ct);
            if (orders.Count == 0) return count;

            foreach (var order in orders)
            {
                var document = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    order.CompanyId, order.BusinessUnitId, BusinessDocumentType.FashionSalesOrder, nameof(SalesOrder),
                    order.Id, order.OrderNumber, order.TotalAmount, order.OrderDate, order.CustomerPartyId,
                    ExternalSourceSystem: order.SourceSystem, ExternalSourceId: order.SourceOrderId), ct);
                order.BusinessDocumentId = document.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillSalesDocumentsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var documents = await db.SalesDocuments
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.IssuedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (documents.Count == 0) return count;

            foreach (var document in documents)
            {
                var registryDocument = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    document.CompanyId, document.BusinessUnitId, BusinessDocumentType.FashionSalesDocument, nameof(SalesDocument),
                    document.Id, document.DocumentNumber, document.TotalAmount, document.IssuedAt, document.CustomerPartyId), ct);
                document.BusinessDocumentId = registryDocument.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillReturnsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var returns = await db.Returns
                .Include(x => x.SalesOrder)
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.ReturnedAt)
                .Take(batchSize)
                .ToListAsync(ct);
            if (returns.Count == 0) return count;

            foreach (var returnEntity in returns)
            {
                var status = returnEntity.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                    ? BusinessDocumentStatus.Voided
                    : returnEntity.Status.Equals("Inspecting", StringComparison.OrdinalIgnoreCase)
                        ? BusinessDocumentStatus.Draft
                        : BusinessDocumentStatus.Open;
                var document = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    returnEntity.CompanyId,
                    returnEntity.BusinessUnitId,
                    BusinessDocumentType.FashionReturn,
                    nameof(Return),
                    returnEntity.Id,
                    returnEntity.ReturnNumber,
                    returnEntity.TotalRefundAmount,
                    returnEntity.UpdatedAt ?? returnEntity.ReturnedAt,
                    returnEntity.SalesOrder.CustomerPartyId,
                    Status: status,
                    ExternalSourceSystem: returnEntity.SalesOrder.SourceSystem,
                    ExternalSourceId: returnEntity.SalesOrder.SourceOrderId), ct);
                returnEntity.BusinessDocumentId = document.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillPayrollPeriodsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var periods = await db.PayrollPeriods
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.StartDate)
                .Take(batchSize)
                .ToListAsync(ct);
            if (periods.Count == 0) return count;

            foreach (var period in periods)
            {
                var document = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    period.CompanyId, period.BusinessUnitId, BusinessDocumentType.PayrollPeriod, nameof(PayrollPeriod),
                    period.Id, $"PAYROLL-{period.Id:N}", period.TotalNetAmount,
                    period.StartDate.ToDateTime(TimeOnly.MinValue), Status: ToDocumentStatus(period.Status)), ct);
                period.BusinessDocumentId = document.Id;
                count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<(int DocumentsLinked, int DocumentLinksCreated)> BackfillFinanceTransactionsAsync(int batchSize, CancellationToken ct)
    {
        var documentsLinked = 0;
        var documentLinksCreated = 0;
        while (true)
        {
            var transactions = await db.FinanceTransactions
                .Where(x => x.BusinessDocumentId == null)
                .OrderBy(x => x.TransactionDate)
                .Take(batchSize)
                .ToListAsync(ct);
            if (transactions.Count == 0) return (documentsLinked, documentLinksCreated);

            foreach (var transaction in transactions)
            {
                var document = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                    transaction.CompanyId, transaction.BusinessUnitId, BusinessDocumentType.FinanceTransaction,
                    nameof(FinanceTransaction), transaction.Id, $"FIN-{transaction.Id:N}", transaction.Amount,
                    transaction.TransactionDate, Status: BusinessDocumentStatus.Settled), ct);
                transaction.BusinessDocumentId = document.Id;
                documentsLinked++;

                if (transaction.ReferenceType is not null && transaction.ReferenceId.HasValue)
                {
                    var sourceDocument = await db.BusinessDocuments.FirstOrDefaultAsync(x =>
                        x.CompanyId == transaction.CompanyId
                        && x.SourceEntityType == transaction.ReferenceType
                        && x.SourceEntityId == transaction.ReferenceId.Value, ct);
                    if (sourceDocument is not null && sourceDocument.Id != document.Id
                        && !await db.BusinessDocumentLinks.AnyAsync(x =>
                            x.FromDocumentId == document.Id && x.ToDocumentId == sourceDocument.Id
                            && x.LinkType == BusinessDocumentLinkType.Settlement, ct))
                    {
                        db.BusinessDocumentLinks.Add(new BusinessDocumentLink
                        {
                            FromDocumentId = document.Id,
                            ToDocumentId = sourceDocument.Id,
                            LinkType = BusinessDocumentLinkType.Settlement,
                            Amount = transaction.Amount,
                            LinkedAt = transaction.TransactionDate,
                            Notes = transaction.Description
                        });
                        documentLinksCreated++;
                    }
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillConfirmedOperationalPostingsAsync(int batchSize, CancellationToken ct)
    {
        if (financePostingService is null)
        {
            return 0;
        }

        var posted = 0;
        posted += await BackfillPaymentPostingsAsync(batchSize, ct);
        posted += await BackfillCscaEnrollmentPostingsAsync(batchSize, ct);
        posted += await BackfillInterviewPostingsAsync(batchSize, ct);
        posted += await BackfillPayrollPostingsAsync(batchSize, ct);
        return posted;
    }

    private async Task<int> BackfillPaymentPostingsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var payments = await db.Payments
                .Where(x => x.Amount > 0 &&
                    ((x.Status == PaymentStatus.Paid || x.Status == PaymentStatus.Partial) &&
                     !db.FinanceTransactions.Any(f => f.CompanyId == x.CompanyId && f.ReferenceType == nameof(Payment) &&
                         f.ReferenceId == x.Id && f.TransactionType == TransactionType.Income) ||
                    (x.Status == PaymentStatus.Refunded &&
                     !db.FinanceTransactions.Any(f => f.CompanyId == x.CompanyId && f.ReferenceType == nameof(Payment) &&
                         f.ReferenceId == x.Id && f.TransactionType == TransactionType.Expense))))
                .OrderBy(x => x.PaidAt).Take(batchSize).ToListAsync(ct);
            if (payments.Count == 0) return count;

            foreach (var payment in payments)
            {
                var transactionType = payment.Status == PaymentStatus.Refunded ? TransactionType.Expense : TransactionType.Income;
                var outcome = await financePostingService!.PostAsync(new FinancePostingRequest(
                    payment.CompanyId, payment.BusinessUnitId, transactionType, payment.Amount, payment.PaidAt,
                    nameof(Payment), payment.Id,
                    transactionType == TransactionType.Income ? $"Thu thanh toán EdTech ({payment.SourcePaymentId})" : $"Hoàn tiền EdTech ({payment.SourcePaymentId})",
                    payment.BusinessDocumentId), ct);
                if (outcome.Outcome == FinancePostingOutcome.Created) count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillCscaEnrollmentPostingsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var students = await db.CscaClassStudents.Include(x => x.Class)
                .Where(x => x.PaidAmount > 0 &&
                    ((x.PaymentStatus == PaymentStatus.Paid || x.PaymentStatus == PaymentStatus.Partial) &&
                     !db.FinanceTransactions.Any(f => f.CompanyId == x.Class.CompanyId && f.ReferenceType == nameof(CscaClassStudent) &&
                         f.ReferenceId == x.Id && f.TransactionType == TransactionType.Income) ||
                    (x.PaymentStatus == PaymentStatus.Refunded &&
                     !db.FinanceTransactions.Any(f => f.CompanyId == x.Class.CompanyId && f.ReferenceType == nameof(CscaClassStudent) &&
                         f.ReferenceId == x.Id && f.TransactionType == TransactionType.Expense))))
                .OrderBy(x => x.JoinedAt).Take(batchSize).ToListAsync(ct);
            if (students.Count == 0) return count;

            foreach (var student in students)
            {
                var transactionType = student.PaymentStatus == PaymentStatus.Refunded ? TransactionType.Expense : TransactionType.Income;
                var outcome = await financePostingService!.PostAsync(new FinancePostingRequest(
                    student.Class.CompanyId, student.Class.BusinessUnitId, transactionType, student.PaidAmount,
                    student.UpdatedAt ?? student.JoinedAt, nameof(CscaClassStudent), student.Id,
                    transactionType == TransactionType.Income ? $"Thu học phí CSCA {student.StudentName} ({student.Class.Code})" : $"Hoàn học phí CSCA {student.StudentName} ({student.Class.Code})",
                    student.BusinessDocumentId), ct);
                if (outcome.Outcome == FinancePostingOutcome.Created) count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillInterviewPostingsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var customers = await db.InterviewCustomers
                .Where(x => x.PaidAmount > 0 &&
                    ((x.Status == PaymentStatus.Paid || x.Status == PaymentStatus.Partial) &&
                     !db.FinanceTransactions.Any(f => f.CompanyId == x.CompanyId && f.ReferenceType == nameof(InterviewCustomer) &&
                         f.ReferenceId == x.Id && f.TransactionType == TransactionType.Income) ||
                    (x.Status == PaymentStatus.Refunded &&
                     !db.FinanceTransactions.Any(f => f.CompanyId == x.CompanyId && f.ReferenceType == nameof(InterviewCustomer) &&
                         f.ReferenceId == x.Id && f.TransactionType == TransactionType.Expense))))
                .OrderBy(x => x.CreatedAt).Take(batchSize).ToListAsync(ct);
            if (customers.Count == 0) return count;

            foreach (var customer in customers)
            {
                var transactionType = customer.Status == PaymentStatus.Refunded ? TransactionType.Expense : TransactionType.Income;
                var outcome = await financePostingService!.PostAsync(new FinancePostingRequest(
                    customer.CompanyId, customer.BusinessUnitId, transactionType, customer.PaidAmount,
                    customer.UpdatedAt ?? customer.CreatedAt, nameof(InterviewCustomer), customer.Id,
                    transactionType == TransactionType.Income ? $"Thu dịch vụ Mock Interview {customer.FullName} ({customer.PackageName})" : $"Hoàn tiền dịch vụ Mock Interview {customer.FullName} ({customer.PackageName})",
                    customer.BusinessDocumentId), ct);
                if (outcome.Outcome == FinancePostingOutcome.Created) count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<int> BackfillPayrollPostingsAsync(int batchSize, CancellationToken ct)
    {
        var count = 0;
        while (true)
        {
            var periods = await db.PayrollPeriods
                .Where(x => x.Status == PayrollStatus.Paid && x.TotalNetAmount > 0 &&
                    !db.FinanceTransactions.Any(f => f.CompanyId == x.CompanyId && f.ReferenceType == nameof(PayrollPeriod) &&
                        f.ReferenceId == x.Id && f.TransactionType == TransactionType.Expense))
                .OrderBy(x => x.StartDate).Take(batchSize).ToListAsync(ct);
            if (periods.Count == 0) return count;

            foreach (var period in periods)
            {
                var outcome = await financePostingService!.PostAsync(new FinancePostingRequest(
                    period.CompanyId, period.BusinessUnitId, TransactionType.Expense, period.TotalNetAmount,
                    period.EndDate.ToDateTime(TimeOnly.MaxValue), nameof(PayrollPeriod), period.Id,
                    $"Chi lương kỳ {period.Name}", period.BusinessDocumentId), ct);
                if (outcome.Outcome == FinancePostingOutcome.Created) count++;
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private static BusinessDocumentStatus ToDocumentStatus(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => BusinessDocumentStatus.Settled,
        PaymentStatus.Failed or PaymentStatus.Refunded or PaymentStatus.Cancelled => BusinessDocumentStatus.Voided,
        _ => BusinessDocumentStatus.Open
    };

    private static BusinessDocumentStatus ToDocumentStatus(PayrollStatus status) => status switch
    {
        PayrollStatus.Draft => BusinessDocumentStatus.Draft,
        PayrollStatus.Paid or PayrollStatus.Published => BusinessDocumentStatus.Settled,
        _ => BusinessDocumentStatus.Open
    };
}
