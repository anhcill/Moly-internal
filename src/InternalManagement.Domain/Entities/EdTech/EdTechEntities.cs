using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Domain.Entities.EdTech;

public class Course : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string CourseSourceId { get; set; } = string.Empty; // ID on original website
    public string Title { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = "Published"; // Draft, Published, Archived
    public int Version { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public ICollection<CourseModule> Modules { get; set; } = new List<CourseModule>();
    public ICollection<CscaClass> CscaClasses { get; set; } = new List<CscaClass>();
}

public class CourseModule : BaseEntity
{
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
}

public class Subject : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
}

public class Topic : BaseEntity
{
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
}

public class QuestionBank : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<Question> Questions { get; set; } = new List<Question>();
}

public class Question : BaseEntity, IAuditableEntity
{
    public string? SourceSystem { get; set; }
    public string? SourceId { get; set; }

    public Guid QuestionBankId { get; set; }
    public QuestionBank QuestionBank { get; set; } = null!;

    public Guid? SubjectId { get; set; }
    public Subject? Subject { get; set; }

    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }

    public string DifficultyLevel { get; set; } = "Medium"; // Easy, Medium, Hard
    public Guid? CurrentVersionId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<QuestionVersion> Versions { get; set; } = new List<QuestionVersion>();
    public ICollection<QuestionTag> Tags { get; set; } = new List<QuestionTag>();
}

public class QuestionVersion : BaseEntity
{
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    public int VersionNumber { get; set; } = 1;
    public string ContentHtml { get; set; } = string.Empty;
    public string? ExplanationHtml { get; set; }
    public QuestionPublicationStatus Status { get; set; } = QuestionPublicationStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }

    public ICollection<QuestionChoice> Choices { get; set; } = new List<QuestionChoice>();
    public ICollection<ContentPublication> Publications { get; set; } = new List<ContentPublication>();
}

public class QuestionChoice : BaseEntity
{
    public Guid QuestionVersionId { get; set; }
    public QuestionVersion QuestionVersion { get; set; } = null!;

    public string Label { get; set; } = string.Empty; // A, B, C, D
    public string ContentHtml { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int OrderIndex { get; set; }
}

public class QuestionTag : BaseEntity
{
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;

    public string Tag { get; set; } = string.Empty;
}

public class ContentPublication : BaseEntity
{
    public Guid QuestionVersionId { get; set; }
    public QuestionVersion QuestionVersion { get; set; } = null!;

    public string PublishedBy { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

public class EdTechCustomer : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? PartyId { get; set; }
    public Party? Party { get; set; }

    public string SourceSystem { get; set; } = "CSCA_MOLI_STUDIO";
    public string SourceId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public class Subscription : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid CustomerId { get; set; }
    public EdTechCustomer Customer { get; set; } = null!;

    public string PackageName { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
}

public class Payment : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public Guid CustomerId { get; set; }
    public EdTechCustomer Customer { get; set; } = null!;

    public string SourceSystem { get; set; } = "CSCA_MOLI_STUDIO";
    public string SourcePaymentId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string? PaymentMethod { get; set; }
    public string? TransactionReference { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
