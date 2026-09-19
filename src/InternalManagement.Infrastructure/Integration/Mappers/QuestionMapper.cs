using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Integration.Mappers;

public sealed class QuestionMapper : IEntityMapper
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<QuestionMapper> _logger;

    public QuestionMapper(IApplicationDbContext db, ILogger<QuestionMapper> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string EntityType => "Questions";

    public async Task<EntityMapResult> MapAndUpsertAsync(
        JsonElement item, string sourceSystem, Guid companyId, Guid businessUnitId, CancellationToken ct)
    {
        ExternalQuestionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExternalQuestionDto>(item.GetRawText(), JsonOpts);
        }
        catch (JsonException ex)
        {
            return new EntityMapResult(MapResultStatus.Failed, null, "JSON_PARSE", ex.Message);
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
            return new EntityMapResult(MapResultStatus.Failed, null, "MISSING_ID", "Question ID is required.");

        if (string.IsNullOrWhiteSpace(dto.ContentHtml))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Question content is required.");

        // SourceId là khóa idempotency của câu hỏi. Các bản ghi cũ chưa có
        // SourceId vẫn được giữ nguyên; mọi câu hỏi từ connector mới sẽ dùng
        // cặp (SourceSystem, SourceId) để không sinh bản ghi trùng khi chạy lại.
        var existing = await _db.Questions.FirstOrDefaultAsync(
            q => q.SourceSystem == sourceSystem && q.SourceId == dto.Id,
            ct);
        if (existing != null)
            return new EntityMapResult(MapResultStatus.Skipped, dto.Id);

        // Resolve hoặc tạo QuestionBank
        var bank = await _db.QuestionBanks
            .FirstOrDefaultAsync(b => b.Name == dto.BankName && b.CompanyId == companyId, ct);

        if (bank == null)
        {
            bank = new QuestionBank
            {
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                Name = dto.BankName,
                CreatedBy = "sync"
            };
            _db.QuestionBanks.Add(bank);
            await _db.SaveChangesAsync(ct);
        }

        // Resolve Subject & Topic (nếu có)
        Guid? subjectId = null;
        Guid? topicId = null;

        if (!string.IsNullOrWhiteSpace(dto.Subject))
        {
            var subject = await _db.Subjects
                .Include(s => s.Topics)
                .FirstOrDefaultAsync(s => s.Name == dto.Subject && s.CompanyId == companyId, ct);

            if (subject == null)
            {
                subject = new Subject
                {
                    CompanyId = companyId,
                    BusinessUnitId = businessUnitId,
                    Code = dto.Subject.ToUpperInvariant().Replace(" ", "_"),
                    Name = dto.Subject
                };
                _db.Subjects.Add(subject);
                await _db.SaveChangesAsync(ct);
            }

            subjectId = subject.Id;

            if (!string.IsNullOrWhiteSpace(dto.Topic))
            {
                var topic = subject.Topics.FirstOrDefault(t => t.Name == dto.Topic);
                if (topic == null)
                {
                    topic = new Topic
                    {
                        SubjectId = subject.Id,
                        Name = dto.Topic
                    };
                    _db.Topics.Add(topic);
                    await _db.SaveChangesAsync(ct);
                }
                topicId = topic.Id;
            }
        }

        // Tạo Question + Version + Choices
        var question = new Question
        {
            SourceSystem = sourceSystem,
            SourceId = dto.Id,
            QuestionBankId = bank.Id,
            SubjectId = subjectId,
            TopicId = topicId,
            DifficultyLevel = dto.DifficultyLevel,
            CreatedBy = "sync"
        };

        _db.Questions.Add(question);
        await _db.SaveChangesAsync(ct);

        var version = new QuestionVersion
        {
            QuestionId = question.Id,
            VersionNumber = 1,
            ContentHtml = dto.ContentHtml,
            ExplanationHtml = dto.ExplanationHtml,
            Status = QuestionPublicationStatus.Draft,
            CreatedBy = "sync"
        };

        _db.QuestionVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        // Update CurrentVersionId
        question.CurrentVersionId = version.Id;
        await _db.SaveChangesAsync(ct);

        // Choices
        foreach (var choiceDto in dto.Choices)
        {
            _db.QuestionChoices.Add(new QuestionChoice
            {
                QuestionVersionId = version.Id,
                Label = choiceDto.Label,
                ContentHtml = choiceDto.ContentHtml,
                IsCorrect = choiceDto.IsCorrect,
                OrderIndex = choiceDto.OrderIndex
            });
        }

        // Tags
        foreach (var tag in dto.Tags)
        {
            _db.QuestionTags.Add(new QuestionTag
            {
                QuestionId = question.Id,
                Tag = tag
            });
        }

        if (dto.Choices.Count > 0 || dto.Tags.Count > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Question created: {SourceId} in bank {Bank}", dto.Id, dto.BankName);
        return new EntityMapResult(MapResultStatus.Written, dto.Id);
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}
