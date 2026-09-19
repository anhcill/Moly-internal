using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.EdTech;

namespace InternalManagement.Infrastructure.Integration.Mappers;

public sealed class CourseMapper : IEntityMapper
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<CourseMapper> _logger;

    public CourseMapper(IApplicationDbContext db, ILogger<CourseMapper> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string EntityType => "Courses";

    public async Task<EntityMapResult> MapAndUpsertAsync(
        JsonElement item, string sourceSystem, Guid companyId, Guid businessUnitId, CancellationToken ct)
    {
        ExternalCourseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExternalCourseDto>(item.GetRawText(), JsonSerializerOptions);
        }
        catch (JsonException ex)
        {
            return new EntityMapResult(MapResultStatus.Failed, null, "JSON_PARSE", ex.Message);
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
            return new EntityMapResult(MapResultStatus.Failed, null, "MISSING_ID", "Course ID is required.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Course title is required.");

        if (dto.Price < 0)
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Course price cannot be negative.");

        // Idempotent: tìm theo CourseSourceId
        var existing = await _db.Courses
            .FirstOrDefaultAsync(c => c.CourseSourceId == dto.Id && c.CompanyId == companyId, ct);

        if (existing != null)
        {
            // Skip nếu version không thay đổi
            if (existing.Version >= dto.Version)
            {
                return new EntityMapResult(MapResultStatus.Skipped, dto.Id);
            }

            // Update nếu version mới hơn
            existing.Title = dto.Title;
            existing.Slug = dto.Slug;
            existing.Description = dto.Description;
            existing.ThumbnailUrl = dto.ThumbnailUrl;
            existing.Price = dto.Price;
            existing.Status = dto.Status;
            existing.Version = dto.Version;
            existing.UpdatedBy = "sync";

            // Update modules
            var existingModules = await _db.CourseModules
                .Where(m => m.CourseId == existing.Id).ToListAsync(ct);
            foreach (var m in existingModules)
                _db.CourseModules.Remove(m);

            foreach (var modDto in dto.Modules)
            {
                _db.CourseModules.Add(new CourseModule
                {
                    CourseId = existing.Id,
                    Title = modDto.Title,
                    OrderIndex = modDto.OrderIndex
                });
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Course updated: {SourceId} → {Title}", dto.Id, dto.Title);
            return new EntityMapResult(MapResultStatus.Written, dto.Id);
        }

        // Insert mới
        var course = new Course
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            CourseSourceId = dto.Id,
            Title = dto.Title,
            Slug = dto.Slug,
            Description = dto.Description,
            ThumbnailUrl = dto.ThumbnailUrl,
            Price = dto.Price,
            Status = dto.Status,
            Version = dto.Version,
            CreatedBy = "sync"
        };

        _db.Courses.Add(course);
        await _db.SaveChangesAsync(ct);

        // Add modules
        foreach (var modDto in dto.Modules)
        {
            _db.CourseModules.Add(new CourseModule
            {
                CourseId = course.Id,
                Title = modDto.Title,
                OrderIndex = modDto.OrderIndex
            });
        }

        if (dto.Modules.Count > 0)
            await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Course created: {SourceId} → {Title}", dto.Id, dto.Title);
        return new EntityMapResult(MapResultStatus.Written, dto.Id);
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
