using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public class BusinessEntityConfigurations :
    IEntityTypeConfiguration<Course>,
    IEntityTypeConfiguration<CourseModule>,
    IEntityTypeConfiguration<Subject>,
    IEntityTypeConfiguration<Topic>,
    IEntityTypeConfiguration<QuestionBank>,
    IEntityTypeConfiguration<Question>,
    IEntityTypeConfiguration<QuestionVersion>,
    IEntityTypeConfiguration<QuestionChoice>,
    IEntityTypeConfiguration<QuestionTag>,
    IEntityTypeConfiguration<ContentPublication>,
    IEntityTypeConfiguration<EdTechCustomer>,
    IEntityTypeConfiguration<Subscription>,
    IEntityTypeConfiguration<Payment>,
    IEntityTypeConfiguration<CscaClass>,
    IEntityTypeConfiguration<CscaClassStudent>,
    IEntityTypeConfiguration<CscaClassStaff>,
    IEntityTypeConfiguration<CscaClassroom>,
    IEntityTypeConfiguration<CscaClassSchedule>,
    IEntityTypeConfiguration<CscaLessonSession>,
    IEntityTypeConfiguration<CscaLessonAttendance>,
    IEntityTypeConfiguration<InterviewCustomer>,
    IEntityTypeConfiguration<ProfitAllocation>,
    IEntityTypeConfiguration<Employee>,
    IEntityTypeConfiguration<AttendanceRecord>,
    IEntityTypeConfiguration<PayrollPeriod>,
    IEntityTypeConfiguration<PayrollPolicyVersion>,
    IEntityTypeConfiguration<PayrollAdjustment>,
    IEntityTypeConfiguration<Payslip>,
    IEntityTypeConfiguration<PayrollApproval>,
    IEntityTypeConfiguration<Warehouse>,
    IEntityTypeConfiguration<Product>,
    IEntityTypeConfiguration<ProductVariant>,
    IEntityTypeConfiguration<Supplier>,
    IEntityTypeConfiguration<PurchaseReceipt>,
    IEntityTypeConfiguration<PurchaseReceiptItem>,
    IEntityTypeConfiguration<InventoryMovement>,
    IEntityTypeConfiguration<InventoryBalance>,
    IEntityTypeConfiguration<SalesOrder>,
    IEntityTypeConfiguration<SalesOrderItem>,
    IEntityTypeConfiguration<Return>,
    IEntityTypeConfiguration<ReturnItem>,
    IEntityTypeConfiguration<SalesSettlement>,
    IEntityTypeConfiguration<SalesDocument>,
    IEntityTypeConfiguration<SalesDocumentItem>,
    IEntityTypeConfiguration<FinanceCategory>,
    IEntityTypeConfiguration<FinanceTransaction>,
    IEntityTypeConfiguration<CashAccount>,
    IEntityTypeConfiguration<MonthlyClosing>
{
    // EdTech
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Price).HasPrecision(18, 2);
        builder.HasMany(c => c.CscaClasses).WithOne(c => c.Course).HasForeignKey(c => c.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(c => !c.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<CourseModule> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Title).HasMaxLength(300).IsRequired();
        builder.HasOne(m => m.Course).WithMany(c => c.Modules).HasForeignKey(m => m.CourseId);
        builder.HasQueryFilter(m => !m.Course.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.CompanyId, s.Code }).IsUnique();
        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<Topic> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.HasOne(t => t.Subject).WithMany(s => s.Topics).HasForeignKey(t => t.SubjectId);
    }

    public void Configure(EntityTypeBuilder<QuestionBank> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.HasKey(q => q.Id);
        builder.HasIndex(q => new { q.SourceSystem, q.SourceId })
            .IsUnique()
            .HasFilter("source_id IS NOT NULL");
        builder.HasOne(q => q.QuestionBank).WithMany(b => b.Questions).HasForeignKey(q => q.QuestionBankId);
        builder.HasOne(q => q.Subject).WithMany().HasForeignKey(q => q.SubjectId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(q => q.Topic).WithMany().HasForeignKey(q => q.TopicId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<QuestionVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => new { v.QuestionId, v.VersionNumber }).IsUnique();
        builder.Property(v => v.Status).HasConversion<string>();
        builder.HasOne(v => v.Question).WithMany(q => q.Versions).HasForeignKey(v => v.QuestionId);
    }

    public void Configure(EntityTypeBuilder<QuestionChoice> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Label).HasMaxLength(10).IsRequired();
        builder.HasOne(c => c.QuestionVersion).WithMany(v => v.Choices).HasForeignKey(c => c.QuestionVersionId);
    }

    public void Configure(EntityTypeBuilder<QuestionTag> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Tag).HasMaxLength(100).IsRequired();
        builder.HasOne(t => t.Question).WithMany(q => q.Tags).HasForeignKey(t => t.QuestionId);
    }

    public void Configure(EntityTypeBuilder<ContentPublication> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasOne(p => p.QuestionVersion).WithMany(v => v.Publications).HasForeignKey(p => p.QuestionVersionId);
    }

    public void Configure(EntityTypeBuilder<EdTechCustomer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.SourceSystem, c.SourceId }).IsUnique();
        builder.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.PackageName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>();
        builder.HasOne(s => s.Customer).WithMany(c => c.Subscriptions).HasForeignKey(s => s.CustomerId);
    }

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.SourceSystem, p.SourcePaymentId }).IsUnique();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>();
        builder.HasOne(p => p.Customer).WithMany(c => c.Payments).HasForeignKey(p => p.CustomerId);
    }

    // CSCA & Interview
    public void Configure(EntityTypeBuilder<CscaClass> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
        builder.Property(c => c.Code).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.TuitionFee).HasPrecision(18, 2);
        builder.HasOne(c => c.Course).WithMany(c => c.CscaClasses).HasForeignKey(c => c.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(c => !c.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<CscaClassStudent> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.StudentName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Hometown).HasMaxLength(200);
        builder.Property(s => s.PaidAmount).HasPrecision(18, 2);
        builder.Property(s => s.DebtDueDate);
        builder.Property(s => s.PaymentStatus).HasConversion<string>();
        builder.HasOne(s => s.Class).WithMany(c => c.Students).HasForeignKey(s => s.ClassId);
        builder.HasQueryFilter(s => !s.Class.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<CscaClassStaff> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CompensationRate).HasPrecision(18, 2);
        builder.HasOne(s => s.Class).WithMany(c => c.Staff).HasForeignKey(s => s.ClassId);
        builder.HasOne(s => s.Employee).WithMany().HasForeignKey(s => s.EmployeeId);
        builder.HasQueryFilter(s => !s.Class.IsDeleted && !s.Employee.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<CscaClassSchedule> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DayOfWeek).IsRequired();
        builder.Property(s => s.StartTime).IsRequired();
        builder.Property(s => s.EndTime).IsRequired();
        builder.Property(s => s.Room).HasMaxLength(200);
        builder.Property(s => s.MeetingUrl).HasMaxLength(1000);
        builder.Property(s => s.Notes).HasMaxLength(1000);
        builder.HasIndex(s => new { s.ClassId, s.DayOfWeek, s.StartTime, s.EndTime }).IsUnique();
        builder.HasOne(s => s.Class).WithMany(c => c.Schedules).HasForeignKey(s => s.ClassId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Classroom).WithMany(c => c.Schedules).HasForeignKey(s => s.ClassroomId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(s => !s.Class.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<CscaClassroom> builder)
    {
        builder.HasKey(room => room.Id);
        builder.HasIndex(room => new { room.CompanyId, room.Code }).IsUnique();
        builder.Property(room => room.Code).HasMaxLength(50).IsRequired();
        builder.Property(room => room.Name).HasMaxLength(200).IsRequired();
        builder.Property(room => room.Location).HasMaxLength(300);
        builder.HasQueryFilter(room => !room.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<CscaLessonSession> builder)
    {
        builder.HasKey(session => session.Id);
        builder.Property(session => session.LessonDate).IsRequired();
        builder.Property(session => session.StartTime).IsRequired();
        builder.Property(session => session.EndTime).IsRequired();
        builder.Property(session => session.Status).HasMaxLength(30).IsRequired();
        builder.Property(session => session.MeetingUrl).HasMaxLength(1000);
        builder.Property(session => session.Notes).HasMaxLength(1000);
        builder.Property(session => session.ExternalSource).HasMaxLength(100);
        builder.Property(session => session.ExternalSessionId).HasMaxLength(128);
        builder.HasIndex(session => new { session.ClassId, session.LessonDate, session.StartTime, session.EndTime }).IsUnique();
        builder.HasIndex(session => new { session.ExternalSource, session.ExternalSessionId }).IsUnique()
            .HasFilter("external_source IS NOT NULL AND external_session_id IS NOT NULL");
        builder.HasIndex(session => new { session.ClassroomId, session.LessonDate, session.StartTime, session.EndTime });
        builder.HasOne(session => session.Class).WithMany(cls => cls.LessonSessions).HasForeignKey(session => session.ClassId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(session => session.Schedule).WithMany(schedule => schedule.LessonSessions).HasForeignKey(session => session.ScheduleId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(session => session.Classroom).WithMany(room => room.LessonSessions).HasForeignKey(session => session.ClassroomId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(session => !session.Class.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<CscaLessonAttendance> builder)
    {
        builder.HasKey(attendance => attendance.Id);
        builder.Property(attendance => attendance.Status).HasMaxLength(30).IsRequired();
        builder.Property(attendance => attendance.Notes).HasMaxLength(1000);
        builder.HasIndex(attendance => new { attendance.LessonSessionId, attendance.StudentId }).IsUnique();
        builder.HasOne(attendance => attendance.LessonSession).WithMany(session => session.Attendances).HasForeignKey(attendance => attendance.LessonSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(attendance => attendance.Student).WithMany(student => student.LessonAttendances).HasForeignKey(attendance => attendance.StudentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(attendance => !attendance.LessonSession.Class.IsDeleted && !attendance.Student.Class.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<InterviewCustomer> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => new { i.SourceSystem, i.SourceId }).IsUnique();
        builder.Property(i => i.FullName).HasMaxLength(200).IsRequired();
        builder.Property(i => i.PaidAmount).HasPrecision(18, 2);
        builder.Property(i => i.Status).HasConversion<string>();
    }

    public void Configure(EntityTypeBuilder<ProfitAllocation> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.IncomeAmount).HasPrecision(18, 2);
        builder.Property(p => p.ExpenseAmount).HasPrecision(18, 2);
        builder.HasIndex(p => new { p.ReferenceType, p.ReferenceId }).IsUnique();
    }

    // HR & Payroll
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.CompanyId, e.EmployeeCode }).IsUnique();
        builder.Property(e => e.EmployeeCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.BaseSalary).HasPrecision(18, 2);
        builder.Property(e => e.EmploymentType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(EmploymentType.FULL_TIME);
        builder.Property(e => e.PartTimeCalculationMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PartTimeUnitRate).HasPrecision(18, 2);
        builder.Property(e => e.CvUrlOrPath).HasMaxLength(1000);
        builder.Property(e => e.ProfessionalSummary).HasMaxLength(4000);
        builder.Property(e => e.Skills).HasMaxLength(4000);
        builder.Property(e => e.Experience).HasMaxLength(8000);
        builder.Property(e => e.StatusReason).HasMaxLength(2000);
        builder.HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.EmployeeId, a.Date }).IsUnique();
        builder.Property(a => a.WorkHours).HasPrecision(4, 2);
        builder.HasOne(a => a.Employee).WithMany(e => e.AttendanceRecords).HasForeignKey(a => a.EmployeeId);
        builder.HasQueryFilter(a => !a.Employee.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>();
        builder.Property(p => p.TotalGrossAmount).HasPrecision(18, 2);
        builder.Property(p => p.TotalNetAmount).HasPrecision(18, 2);
    }

    public void Configure(EntityTypeBuilder<PayrollPolicyVersion> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.CompanyId, p.VersionNumber }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<PayrollAdjustment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Amount).HasPrecision(18, 2);
        builder.HasOne(a => a.PayrollPeriod).WithMany(p => p.Adjustments).HasForeignKey(a => a.PayrollPeriodId);
        builder.HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId);
        builder.HasQueryFilter(a => !a.Employee.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Payslip> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.PayrollPeriodId, p.EmployeeId }).IsUnique();
        builder.Property(p => p.BaseSalary).HasPrecision(18, 2);
        builder.Property(p => p.StandardWorkDays).HasPrecision(8, 2);
        builder.Property(p => p.ActualWorkDays).HasPrecision(8, 2);
        builder.Property(p => p.EmploymentType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(EmploymentType.FULL_TIME);
        builder.Property(p => p.PartTimeCalculationMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PartTimeUnitRate).HasPrecision(18, 2);
        builder.Property(p => p.ActualWorkHours).HasPrecision(10, 2);
        builder.Property(p => p.ActualShifts).HasPrecision(10, 2);
        builder.Property(p => p.GrossSalary).HasPrecision(18, 2);
        builder.Property(p => p.Allowances).HasPrecision(18, 2);
        builder.Property(p => p.KpiBonus).HasPrecision(18, 2);
        builder.Property(p => p.HealthInsurance).HasPrecision(18, 2);
        builder.Property(p => p.TotalIncome).HasPrecision(18, 2);
        builder.Property(p => p.Deductions).HasPrecision(18, 2);
        builder.Property(p => p.TotalDeductions).HasPrecision(18, 2);
        builder.Property(p => p.NetSalary).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>();
        builder.HasOne(p => p.PayrollPeriod).WithMany(period => period.Payslips).HasForeignKey(p => p.PayrollPeriodId);
        builder.HasOne(p => p.Employee).WithMany(e => e.Payslips).HasForeignKey(p => p.EmployeeId);
        builder.HasQueryFilter(p => !p.Employee.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<PayrollApproval> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<string>();
        builder.HasOne(a => a.PayrollPeriod).WithMany(p => p.Approvals).HasForeignKey(a => a.PayrollPeriodId);
        builder.HasOne(a => a.Approver).WithMany().HasForeignKey(a => a.ApproverId);
        builder.HasQueryFilter(a => !a.Approver.IsDeleted);
    }

    // Fashion & Inventory
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => new { w.CompanyId, w.Code }).IsUnique();
        builder.HasIndex(w => new { w.CompanyId, w.BusinessUnitId, w.IsDefault })
            .IsUnique()
            .HasFilter("is_default = true AND is_active = true");
        builder.Property(w => w.Code).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.Address).HasMaxLength(500);
    }

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.CompanyId, p.Code }).IsUnique();
        builder.HasIndex(p => new { p.CompanyId, p.BusinessUnitId, p.CreatedAt })
            .HasDatabaseName("ix_products_company_bu_created");
        builder.Property(p => p.Code).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(300).IsRequired();
        builder.HasQueryFilter(p => !p.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => v.Sku).IsUnique();
        builder.HasIndex(v => new { v.ProductId, v.IsActive, v.SourcingType })
            .HasDatabaseName("ix_product_variants_product_active_source");
        builder.Property(v => v.Sku).HasMaxLength(100).IsRequired();
        builder.Property(v => v.SourcingType).HasConversion<string>();
        builder.Property(v => v.CostPrice).HasPrecision(18, 2);
        builder.Property(v => v.CostStatus).HasConversion<string>();
        builder.Property(v => v.SellingPrice).HasPrecision(18, 2);
        builder.HasOne(v => v.Product).WithMany(p => p.Variants).HasForeignKey(v => v.ProductId);
        builder.HasQueryFilter(v => !v.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.CompanyId, s.Code }).IsUnique();
        builder.HasIndex(s => new { s.CompanyId, s.BusinessUnitId, s.CreatedAt })
            .HasDatabaseName("ix_suppliers_company_bu_created");
        builder.Property(s => s.Code).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.PhoneNumbers).HasMaxLength(4000);
        builder.Property(s => s.BankAccounts).HasMaxLength(4000);
    }

    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.CompanyId, r.ReceiptNumber }).IsUnique();
        builder.HasIndex(r => new { r.CompanyId, r.BusinessUnitId, r.CreatedAt })
            .HasDatabaseName("ix_purchase_receipts_company_bu_created");
        builder.Property(r => r.ReceiptNumber).HasMaxLength(100).IsRequired();
        builder.Property(r => r.TotalAmount).HasPrecision(18, 2);
        builder.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId);
        builder.HasOne(r => r.Warehouse).WithMany().HasForeignKey(r => r.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<PurchaseReceiptItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.HasOne(i => i.PurchaseReceipt).WithMany(r => r.Items).HasForeignKey(i => i.PurchaseReceiptId);
        builder.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId);
        builder.HasQueryFilter(i => !i.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.CompanyId, m.WarehouseId, m.MovementDate });
        builder.HasIndex(m => new { m.CompanyId, m.BusinessUnitId, m.WarehouseId, m.MovementDate })
            .HasDatabaseName("ix_inventory_movements_company_bu_warehouse_date");
        builder.HasIndex(m => new { m.CompanyId, m.BusinessUnitId, m.WarehouseId, m.ProductVariantId, m.MovementDate })
            .HasDatabaseName("ix_inventory_movements_company_bu_warehouse_variant_date");
        builder.Property(m => m.MovementType).HasConversion<string>();
        builder.Property(m => m.UnitCost).HasPrecision(18, 2);
        builder.HasOne(m => m.ProductVariant).WithMany().HasForeignKey(m => m.ProductVariantId);
        builder.HasOne(m => m.Warehouse).WithMany(w => w.Movements).HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(m => !m.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => new { b.CompanyId, b.WarehouseId, b.ProductVariantId }).IsUnique();
        builder.HasIndex(b => new { b.CompanyId, b.BusinessUnitId, b.WarehouseId, b.LastUpdated })
            .HasDatabaseName("ix_inventory_balances_company_bu_warehouse_updated");
        builder.Property(b => b.Version).IsRowVersion();
        builder.HasOne(b => b.ProductVariant).WithMany(v => v.Balances).HasForeignKey(b => b.ProductVariantId);
        builder.HasOne(b => b.Warehouse).WithMany(w => w.Balances).HasForeignKey(b => b.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(b => !b.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.HasKey(o => o.Id);
        builder.HasIndex(o => new { o.CompanyId, o.OrderNumber }).IsUnique();
        builder.HasIndex(o => new { o.CompanyId, o.SourceSystem, o.SourceOrderId })
            .IsUnique()
            .HasFilter("source_order_id IS NOT NULL");
        builder.Property(o => o.OrderNumber).HasMaxLength(100).IsRequired();
        foreach (var property in new[] { nameof(SalesOrder.TotalAmount), nameof(SalesOrder.GrossAmount), nameof(SalesOrder.DiscountAmount), nameof(SalesOrder.ShippingCustomerPaid), nameof(SalesOrder.ShippingShopSubsidy), nameof(SalesOrder.TaxAmount), nameof(SalesOrder.PlatformFee), nameof(SalesOrder.AffiliateFee), nameof(SalesOrder.PaymentFee), nameof(SalesOrder.AdvertisingCost), nameof(SalesOrder.PackagingCost), nameof(SalesOrder.OtherSellingExpense), nameof(SalesOrder.ActualCogs), nameof(SalesOrder.NetRevenue), nameof(SalesOrder.Profit) })
        {
            builder.Property<decimal>(property).HasPrecision(18, 6);
        }
        builder.Property(o => o.SourceSystem).HasMaxLength(30).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>();
        builder.Property(o => o.CostStatus).HasConversion<string>();
        builder.HasOne(o => o.Warehouse).WithMany().HasForeignKey(o => o.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<SalesOrderItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.UnitCostSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(100).IsRequired();
        builder.Property(i => i.ProductNameSnapshot).HasMaxLength(300).IsRequired();
        builder.HasOne(i => i.SalesOrder).WithMany(o => o.Items).HasForeignKey(i => i.SalesOrderId);
        builder.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId);
        builder.HasQueryFilter(i => !i.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Return> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.CompanyId, r.ReturnNumber }).IsUnique();
        builder.Property(r => r.ReturnNumber).HasMaxLength(100).IsRequired();
        builder.Property(r => r.TotalRefundAmount).HasPrecision(18, 2);
        builder.HasOne(r => r.SalesOrder).WithMany(o => o.Returns).HasForeignKey(r => r.SalesOrderId);
        builder.HasOne(r => r.BusinessDocument).WithMany().HasForeignKey(r => r.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<ReturnItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.UnitPriceSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.UnitCostSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.RefundAmount).HasPrecision(18, 2);
        builder.HasOne(i => i.Return).WithMany(r => r.Items).HasForeignKey(i => i.ReturnId);
        builder.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId);
        builder.HasQueryFilter(i => !i.ProductVariant.Product.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<SalesSettlement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.PaymentReference }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SalesOrderId, x.Status, x.OccurredAt });
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PaymentReference).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(100);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasOne(x => x.SalesOrder).WithMany(x => x.Settlements).HasForeignKey(x => x.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SalesDocument).WithMany().HasForeignKey(x => x.SalesDocumentId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Return).WithMany().HasForeignKey(x => x.ReturnId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<SalesDocument> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SalesOrderId, x.DocumentType }).IsUnique();
        builder.Property(x => x.DocumentNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DocumentType).HasConversion<string>();
        foreach (var property in new[] { nameof(SalesDocument.GrossAmount), nameof(SalesDocument.DiscountAmount), nameof(SalesDocument.TaxAmount), nameof(SalesDocument.TotalAmount) })
        {
            builder.Property<decimal>(property).HasPrecision(18, 6);
        }
        builder.HasOne(x => x.SalesOrder).WithMany(x => x.Documents).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<SalesDocumentItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SkuSnapshot).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProductNameSnapshot).HasMaxLength(300).IsRequired();
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.TaxAmount).HasPrecision(18, 6);
        builder.Property(x => x.LineTotal).HasPrecision(18, 6);
        builder.HasOne(x => x.SalesDocument).WithMany(x => x.Items).HasForeignKey(x => x.SalesDocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(x => !x.ProductVariant.Product.IsDeleted);
    }

    // Finance
    public void Configure(EntityTypeBuilder<FinanceCategory> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
        builder.Property(c => c.Code).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>();
    }

    public void Configure(EntityTypeBuilder<FinanceTransaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => new { t.CompanyId, t.TransactionDate });
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.TransactionType).HasConversion<string>();
        builder.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<CashAccount> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.CompanyId, a.Code }).IsUnique();
        builder.Property(a => a.Code).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.CurrentBalance).HasPrecision(18, 2);
    }

    public void Configure(EntityTypeBuilder<MonthlyClosing> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.CompanyId, m.BusinessUnitId, m.Year, m.Month }).IsUnique();
        builder.Property(m => m.TotalIncome).HasPrecision(18, 2);
        builder.Property(m => m.TotalExpense).HasPrecision(18, 2);
    }
}
