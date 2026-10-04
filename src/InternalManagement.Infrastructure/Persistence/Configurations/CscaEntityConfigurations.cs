using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public sealed class CscaEntityConfigurations :
    IEntityTypeConfiguration<CscaClass>,
    IEntityTypeConfiguration<CscaClassStudent>,
    IEntityTypeConfiguration<CscaClassStaff>,
    IEntityTypeConfiguration<CscaClassroom>,
    IEntityTypeConfiguration<CscaClassSchedule>,
    IEntityTypeConfiguration<CscaLessonSession>,
    IEntityTypeConfiguration<CscaLessonAttendance>,
    IEntityTypeConfiguration<InterviewCustomer>,
    IEntityTypeConfiguration<ProfitAllocation>
{
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
        builder.Property(s => s.DiscountAmount).HasPrecision(18, 2);
        builder.Property(s => s.DiscountNote).HasMaxLength(500);
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
        builder.Property(s => s.ExternalSource).HasMaxLength(100);
        builder.Property(s => s.ExternalScheduleId).HasMaxLength(128);
        builder.Property(s => s.Title).HasMaxLength(255);
        builder.Property(s => s.Timezone).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(30).IsRequired();
        builder.HasIndex(s => new { s.ClassId, s.DayOfWeek, s.StartTime, s.EndTime }).IsUnique();
        builder.HasIndex(s => new { s.ExternalSource, s.ExternalScheduleId }).IsUnique()
            .HasFilter("external_source IS NOT NULL AND external_schedule_id IS NOT NULL");
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

}
