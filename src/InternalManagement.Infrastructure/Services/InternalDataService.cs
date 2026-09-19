using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.InternalData.DTOs;
using InternalManagement.Application.Features.InternalData.Services;
using InternalManagement.Domain.Entities.InternalData;

namespace InternalManagement.Infrastructure.Services;

public sealed class InternalDataService : IInternalDataService
{
    private const int MaximumPageSize = 200;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<InternalDataService> _logger;

    public InternalDataService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<InternalDataService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<PaginatedResult<InternalCustomerDto>>> GetCustomersAsync(
        BusinessSegment segment, string? search, string? source, string? status,
        int pageIndex, int pageSize, CancellationToken ct)
    {
        var paginationError = ValidatePagination(pageIndex, pageSize);
        if (paginationError != null)
            return Result<PaginatedResult<InternalCustomerDto>>.Failure(paginationError);

        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<PaginatedResult<InternalCustomerDto>>.Failure(scopeResult.Errors);

        InternalCustomerStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseCustomerStatus(status, out var value))
                return Result<PaginatedResult<InternalCustomerDto>>.Failure("Trạng thái khách hàng không hợp lệ. Dùng LEAD, ACTIVE, INACTIVE hoặc ARCHIVED.");
            parsedStatus = value;
        }

        var scope = scopeResult.Value!;
        var query = _db.InternalCustomers.AsNoTracking().Where(x =>
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim().ToLower();
            query = query.Where(x =>
                x.Code.ToLower().Contains(keyword) ||
                x.Name.ToLower().Contains(keyword) ||
                (x.ContactPerson != null && x.ContactPerson.ToLower().Contains(keyword)) ||
                (x.Email != null && x.Email.ToLower().Contains(keyword)) ||
                (x.Phone != null && x.Phone.Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var normalizedSource = source.Trim().ToLower();
            query = query.Where(x => x.Source != null && x.Source.ToLower() == normalizedSource);
        }

        if (parsedStatus.HasValue)
            query = query.Where(x => x.Status == parsedStatus.Value);

        var totalCount = await query.CountAsync(ct);
        var entities = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Result<PaginatedResult<InternalCustomerDto>>.Success(
            new PaginatedResult<InternalCustomerDto>(entities.Select(MapCustomer).ToList(), totalCount, pageIndex, pageSize));
    }

    public async Task<Result<InternalCustomerDto>> GetCustomerByIdAsync(
        BusinessSegment segment, Guid id, CancellationToken ct)
    {
        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<InternalCustomerDto>.Failure(scopeResult.Errors);

        var scope = scopeResult.Value!;
        var entity = await _db.InternalCustomers.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == id &&
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment, ct);

        return entity == null
            ? Result<InternalCustomerDto>.Failure("Không tìm thấy khách hàng trong đúng mảng dữ liệu hiện tại.")
            : Result<InternalCustomerDto>.Success(MapCustomer(entity));
    }

    public async Task<Result<InternalCustomerDto>> CreateCustomerAsync(
        BusinessSegment segment, CreateInternalCustomerRequest request, CancellationToken ct)
    {
        var validationErrors = ValidateCustomer(request.Code, request.Name, request.ContactPerson, request.Email,
            request.Phone, request.Address, request.Source, request.Status, request.Notes, out var parsedStatus);
        if (validationErrors.Length > 0)
            return Result<InternalCustomerDto>.Failure(validationErrors);

        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<InternalCustomerDto>.Failure(scopeResult.Errors);

        var scope = scopeResult.Value!;
        var code = NormalizeCode(request.Code);
        var duplicated = await _db.InternalCustomers.AnyAsync(x =>
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment &&
            x.Code == code, ct);
        if (duplicated)
            return Result<InternalCustomerDto>.Failure($"Mã khách hàng '{code}' đã tồn tại trong mảng {InternalDataContract.SegmentName(segment)}.");

        var entity = new InternalCustomer
        {
            CompanyId = scope.CompanyId,
            BusinessUnitId = scope.BusinessUnitId,
            BusinessSegment = segment,
            Code = code,
            Name = request.Name.Trim(),
            ContactPerson = Clean(request.ContactPerson),
            Email = Clean(request.Email)?.ToLowerInvariant(),
            Phone = Clean(request.Phone),
            Address = Clean(request.Address),
            Source = Clean(request.Source),
            Status = parsedStatus,
            Notes = Clean(request.Notes)
        };

        _db.InternalCustomers.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Đã tạo khách hàng nội bộ {CustomerCode} thuộc {Segment}", entity.Code, segment);
        return Result<InternalCustomerDto>.Success(MapCustomer(entity));
    }

    public async Task<Result<InternalCustomerDto>> UpdateCustomerAsync(
        BusinessSegment segment, Guid id, UpdateInternalCustomerRequest request, CancellationToken ct)
    {
        var validationErrors = ValidateCustomer(request.Code, request.Name, request.ContactPerson, request.Email,
            request.Phone, request.Address, request.Source, request.Status, request.Notes, out var parsedStatus);
        if (validationErrors.Length > 0)
            return Result<InternalCustomerDto>.Failure(validationErrors);

        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<InternalCustomerDto>.Failure(scopeResult.Errors);

        var scope = scopeResult.Value!;
        var entity = await _db.InternalCustomers.FirstOrDefaultAsync(x =>
            x.Id == id &&
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment, ct);
        if (entity == null)
            return Result<InternalCustomerDto>.Failure("Không tìm thấy khách hàng trong đúng mảng dữ liệu hiện tại.");

        var code = NormalizeCode(request.Code);
        var duplicated = await _db.InternalCustomers.AnyAsync(x =>
            x.Id != id &&
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment &&
            x.Code == code, ct);
        if (duplicated)
            return Result<InternalCustomerDto>.Failure($"Mã khách hàng '{code}' đã tồn tại trong mảng {InternalDataContract.SegmentName(segment)}.");

        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.ContactPerson = Clean(request.ContactPerson);
        entity.Email = Clean(request.Email)?.ToLowerInvariant();
        entity.Phone = Clean(request.Phone);
        entity.Address = Clean(request.Address);
        entity.Source = Clean(request.Source);
        entity.Status = parsedStatus;
        entity.Notes = Clean(request.Notes);

        await _db.SaveChangesAsync(ct);
        return Result<InternalCustomerDto>.Success(MapCustomer(entity));
    }

    public async Task<Result<bool>> DeleteCustomerAsync(BusinessSegment segment, Guid id, CancellationToken ct)
    {
        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<bool>.Failure(scopeResult.Errors);

        var scope = scopeResult.Value!;
        var entity = await _db.InternalCustomers.FirstOrDefaultAsync(x =>
            x.Id == id &&
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment, ct);
        if (entity == null)
            return Result<bool>.Failure("Không tìm thấy khách hàng trong đúng mảng dữ liệu hiện tại.");

        _db.InternalCustomers.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<PaginatedResult<InternalResourceDto>>> GetResourcesAsync(
        BusinessSegment segment, string? search, string? resourceType, string? status, string? tag,
        int pageIndex, int pageSize, CancellationToken ct)
    {
        var paginationError = ValidatePagination(pageIndex, pageSize);
        if (paginationError != null)
            return Result<PaginatedResult<InternalResourceDto>>.Failure(paginationError);

        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<PaginatedResult<InternalResourceDto>>.Failure(scopeResult.Errors);

        InternalResourceType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            if (!TryParseResourceType(resourceType, out var value) || !IsResourceTypeAllowed(segment, value))
                return Result<PaginatedResult<InternalResourceDto>>.Failure(ResourceTypeError(segment));
            parsedType = value;
        }

        InternalResourceStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!TryParseResourceStatus(status, out var value))
                return Result<PaginatedResult<InternalResourceDto>>.Failure("Trạng thái tài liệu không hợp lệ. Dùng DRAFT, ACTIVE hoặc ARCHIVED.");
            parsedStatus = value;
        }

        var scope = scopeResult.Value!;
        var query = _db.InternalResources.AsNoTracking().Where(x =>
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim().ToLower();
            query = query.Where(x =>
                x.Code.ToLower().Contains(keyword) ||
                x.Title.ToLower().Contains(keyword) ||
                (x.FileName != null && x.FileName.ToLower().Contains(keyword)) ||
                (x.Notes != null && x.Notes.ToLower().Contains(keyword)) ||
                x.TagsCsv.Contains(keyword));
        }

        if (parsedType.HasValue)
            query = query.Where(x => x.ResourceType == parsedType.Value);
        if (parsedStatus.HasValue)
            query = query.Where(x => x.Status == parsedStatus.Value);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalizedTag = NormalizeTag(tag);
            query = query.Where(x => x.TagsCsv.Contains($"|{normalizedTag}|"));
        }

        var totalCount = await query.CountAsync(ct);
        var entities = await query
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .ThenBy(x => x.Code)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Result<PaginatedResult<InternalResourceDto>>.Success(
            new PaginatedResult<InternalResourceDto>(entities.Select(MapResource).ToList(), totalCount, pageIndex, pageSize));
    }

    public async Task<Result<InternalResourceDto>> GetResourceByIdAsync(
        BusinessSegment segment, Guid id, CancellationToken ct)
    {
        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<InternalResourceDto>.Failure(scopeResult.Errors);

        var scope = scopeResult.Value!;
        var entity = await _db.InternalResources.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == id &&
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment, ct);

        return entity == null
            ? Result<InternalResourceDto>.Failure("Không tìm thấy tài liệu trong đúng mảng dữ liệu hiện tại.")
            : Result<InternalResourceDto>.Success(MapResource(entity));
    }

    public async Task<Result<InternalResourceDto>> CreateResourceAsync(
        BusinessSegment segment, CreateInternalResourceRequest request, CancellationToken ct)
    {
        var validationErrors = ValidateResource(segment, request.Code, request.Title, request.ResourceType,
            request.StorageUri, request.FileName, request.ContentType, request.FileSizeBytes, request.ChecksumSha256,
            request.Version, request.Status, request.Tags, request.MetadataJson, request.Notes,
            out var parsedType, out var parsedStatus);
        if (validationErrors.Length > 0)
            return Result<InternalResourceDto>.Failure(validationErrors);

        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<InternalResourceDto>.Failure(scopeResult.Errors);

        var scope = scopeResult.Value!;
        var code = NormalizeCode(request.Code);
        var duplicated = await _db.InternalResources.AnyAsync(x =>
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment &&
            x.Code == code, ct);
        if (duplicated)
            return Result<InternalResourceDto>.Failure($"Mã tài liệu '{code}' đã tồn tại trong mảng {InternalDataContract.SegmentName(segment)}.");

        var entity = new InternalResource
        {
            CompanyId = scope.CompanyId,
            BusinessUnitId = scope.BusinessUnitId,
            BusinessSegment = segment,
            Code = code,
            Title = request.Title.Trim(),
            ResourceType = parsedType,
            StorageUri = request.StorageUri.Trim(),
            FileName = Clean(request.FileName),
            ContentType = Clean(request.ContentType)?.ToLowerInvariant(),
            FileSizeBytes = request.FileSizeBytes,
            ChecksumSha256 = Clean(request.ChecksumSha256)?.ToLowerInvariant(),
            Version = request.Version.Trim(),
            Status = parsedStatus,
            TagsCsv = SerializeTags(request.Tags),
            MetadataJson = Clean(request.MetadataJson),
            Notes = Clean(request.Notes)
        };

        _db.InternalResources.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Đã tạo metadata tài liệu {ResourceCode} thuộc {Segment}; binary không lưu trong DB", entity.Code, segment);
        return Result<InternalResourceDto>.Success(MapResource(entity));
    }

    public async Task<Result<InternalResourceDto>> UpdateResourceAsync(
        BusinessSegment segment, Guid id, UpdateInternalResourceRequest request, CancellationToken ct)
    {
        var validationErrors = ValidateResource(segment, request.Code, request.Title, request.ResourceType,
            request.StorageUri, request.FileName, request.ContentType, request.FileSizeBytes, request.ChecksumSha256,
            request.Version, request.Status, request.Tags, request.MetadataJson, request.Notes,
            out var parsedType, out var parsedStatus);
        if (validationErrors.Length > 0)
            return Result<InternalResourceDto>.Failure(validationErrors);

        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<InternalResourceDto>.Failure(scopeResult.Errors);

        var scope = scopeResult.Value!;
        var entity = await _db.InternalResources.FirstOrDefaultAsync(x =>
            x.Id == id &&
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment, ct);
        if (entity == null)
            return Result<InternalResourceDto>.Failure("Không tìm thấy tài liệu trong đúng mảng dữ liệu hiện tại.");

        var code = NormalizeCode(request.Code);
        var duplicated = await _db.InternalResources.AnyAsync(x =>
            x.Id != id &&
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment &&
            x.Code == code, ct);
        if (duplicated)
            return Result<InternalResourceDto>.Failure($"Mã tài liệu '{code}' đã tồn tại trong mảng {InternalDataContract.SegmentName(segment)}.");

        entity.Code = code;
        entity.Title = request.Title.Trim();
        entity.ResourceType = parsedType;
        entity.StorageUri = request.StorageUri.Trim();
        entity.FileName = Clean(request.FileName);
        entity.ContentType = Clean(request.ContentType)?.ToLowerInvariant();
        entity.FileSizeBytes = request.FileSizeBytes;
        entity.ChecksumSha256 = Clean(request.ChecksumSha256)?.ToLowerInvariant();
        entity.Version = request.Version.Trim();
        entity.Status = parsedStatus;
        entity.TagsCsv = SerializeTags(request.Tags);
        entity.MetadataJson = Clean(request.MetadataJson);
        entity.Notes = Clean(request.Notes);

        await _db.SaveChangesAsync(ct);
        return Result<InternalResourceDto>.Success(MapResource(entity));
    }

    public async Task<Result<bool>> DeleteResourceAsync(BusinessSegment segment, Guid id, CancellationToken ct)
    {
        var scopeResult = await ResolveScopeAsync(segment, ct);
        if (!scopeResult.Succeeded)
            return Result<bool>.Failure(scopeResult.Errors);

        var scope = scopeResult.Value!;
        var entity = await _db.InternalResources.FirstOrDefaultAsync(x =>
            x.Id == id &&
            x.CompanyId == scope.CompanyId &&
            x.BusinessUnitId == scope.BusinessUnitId &&
            x.BusinessSegment == segment, ct);
        if (entity == null)
            return Result<bool>.Failure("Không tìm thấy tài liệu trong đúng mảng dữ liệu hiện tại.");

        _db.InternalResources.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private async Task<Result<InternalDataScope>> ResolveScopeAsync(BusinessSegment segment, CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId == Guid.Empty)
            return Result<InternalDataScope>.Failure("Token đăng nhập thiếu company_id; từ chối truy cập để bảo vệ dữ liệu tenant.");

        var canonicalBusinessUnitCode = segment == BusinessSegment.TECHNOLOGY_EDUCATION ? "EDTECH" : "FASHION";
        var allowedBusinessUnitCodes = segment == BusinessSegment.TECHNOLOGY_EDUCATION
            ? new[] { "EDTECH", "CSCA", "INTERVIEW" }
            : new[] { "FASHION" };

        var canonicalBusinessUnitId = await _db.BusinessUnits
            .Where(x => x.CompanyId == companyId.Value && x.Code == canonicalBusinessUnitCode)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

        if (!canonicalBusinessUnitId.HasValue || canonicalBusinessUnitId == Guid.Empty)
            return Result<InternalDataScope>.Failure($"Chưa cấu hình đơn vị nghiệp vụ chuẩn {canonicalBusinessUnitCode} cho công ty hiện tại.");

        if (_currentUser.BusinessUnitId.HasValue && _currentUser.BusinessUnitId != Guid.Empty)
        {
            var currentBusinessUnit = await _db.BusinessUnits.AsNoTracking().FirstOrDefaultAsync(x =>
                x.Id == _currentUser.BusinessUnitId.Value && x.CompanyId == companyId.Value, ct);
            var isHeadOffice = string.Equals(currentBusinessUnit?.Code, "HQ", StringComparison.OrdinalIgnoreCase);
            if (currentBusinessUnit == null ||
                (!isHeadOffice && !allowedBusinessUnitCodes.Contains(currentBusinessUnit.Code, StringComparer.OrdinalIgnoreCase)))
                return Result<InternalDataScope>.Failure($"Tài khoản hiện tại không thuộc mảng {InternalDataContract.SegmentName(segment)}.");
        }

        // EDTECH is the canonical storage BU for the shared Technology-Education segment.
        // CSCA and INTERVIEW users are authorized members of that segment, but records
        // are deliberately stored under EDTECH so all three units see one shared catalog.
        return Result<InternalDataScope>.Success(new InternalDataScope(companyId.Value, canonicalBusinessUnitId.Value));
    }

    private static string[] ValidateCustomer(
        string code, string name, string? contactPerson, string? email, string? phone,
        string? address, string? source, string status, string? notes,
        out InternalCustomerStatus parsedStatus)
    {
        var errors = new List<string>();
        ValidateCodeAndName(code, name, "khách hàng", errors);

        if (!string.IsNullOrWhiteSpace(email) && (email.Length > 254 || !MailAddress.TryCreate(email.Trim(), out _)))
            errors.Add("Email liên hệ không đúng định dạng.");
        if (!string.IsNullOrWhiteSpace(phone) && (phone.Trim().Length < 6 || phone.Trim().Length > 30))
            errors.Add("Số điện thoại phải có từ 6 đến 30 ký tự.");
        AddLengthError(contactPerson, 200, "Người liên hệ", errors);
        AddLengthError(address, 500, "Địa chỉ", errors);
        AddLengthError(source, 100, "Nguồn khách hàng", errors);
        AddLengthError(notes, 4000, "Ghi chú", errors);
        if (!TryParseCustomerStatus(status, out parsedStatus))
            errors.Add("Trạng thái khách hàng không hợp lệ. Dùng LEAD, ACTIVE, INACTIVE hoặc ARCHIVED.");

        return errors.ToArray();
    }

    private static string[] ValidateResource(
        BusinessSegment segment, string code, string title, string resourceType, string storageUri,
        string? fileName, string? contentType, long? fileSizeBytes, string? checksumSha256,
        string version, string status, IReadOnlyCollection<string>? tags, string? metadataJson, string? notes,
        out InternalResourceType parsedType, out InternalResourceStatus parsedStatus)
    {
        var errors = new List<string>();
        ValidateCodeAndName(code, title, "tài liệu", errors);

        if (!TryParseResourceType(resourceType, out parsedType) || !IsResourceTypeAllowed(segment, parsedType))
            errors.Add(ResourceTypeError(segment));

        if (string.IsNullOrWhiteSpace(storageUri))
            errors.Add("Đường dẫn tệp là bắt buộc; hệ thống chỉ lưu đường dẫn/URI, không lưu binary trong DB.");
        else if (storageUri.Length > 2048 || storageUri.Contains('\0') || storageUri.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            errors.Add("Đường dẫn tệp không hợp lệ. Không được gửi data URI/base64 vào trường storageUri.");

        if (fileSizeBytes < 0)
            errors.Add("Kích thước tệp không được âm.");
        AddLengthError(fileName, 255, "Tên tệp", errors);
        AddLengthError(contentType, 150, "Content type", errors);
        AddLengthError(notes, 4000, "Ghi chú", errors);
        if (string.IsNullOrWhiteSpace(version) || version.Trim().Length > 50)
            errors.Add("Phiên bản là bắt buộc và không được vượt quá 50 ký tự.");
        if (!TryParseResourceStatus(status, out parsedStatus))
            errors.Add("Trạng thái tài liệu không hợp lệ. Dùng DRAFT, ACTIVE hoặc ARCHIVED.");

        var checksum = Clean(checksumSha256);
        if (checksum != null && (checksum.Length != 64 || checksum.Any(c => !Uri.IsHexDigit(c))))
            errors.Add("Checksum SHA-256 phải gồm đúng 64 ký tự hệ thập lục phân.");

        if (tags?.Count > 20 || tags?.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 50 || x.Contains('|')) == true)
            errors.Add("Tối đa 20 tags; mỗi tag từ 1 đến 50 ký tự và không chứa dấu '|'.");

        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            if (metadataJson.Length > 10_000)
            {
                errors.Add("Metadata JSON không được vượt quá 10.000 ký tự.");
            }
            else
            {
                try { using var _ = JsonDocument.Parse(metadataJson); }
                catch (JsonException) { errors.Add("Metadata JSON không đúng định dạng JSON."); }
            }
        }

        return errors.ToArray();
    }

    private static void ValidateCodeAndName(string code, string name, string entityName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
            errors.Add($"Mã {entityName} là bắt buộc và không được vượt quá 50 ký tự.");
        else if (code.Trim().Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            errors.Add($"Mã {entityName} chỉ được chứa chữ, số, dấu '-', '_' hoặc '.'.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 250)
            errors.Add($"Tên {entityName} là bắt buộc và không được vượt quá 250 ký tự.");
    }

    private static void AddLengthError(string? value, int maximumLength, string fieldName, List<string> errors)
    {
        if (value?.Trim().Length > maximumLength)
            errors.Add($"{fieldName} không được vượt quá {maximumLength} ký tự.");
    }

    private static string? ValidatePagination(int pageIndex, int pageSize)
    {
        if (pageIndex < 1) return "pageIndex phải lớn hơn hoặc bằng 1.";
        if (pageSize < 1 || pageSize > MaximumPageSize) return $"pageSize phải từ 1 đến {MaximumPageSize}.";
        return null;
    }

    private static bool TryParseCustomerStatus(string? value, out InternalCustomerStatus status)
    {
        var normalized = NormalizeAlias(value);
        var canonical = normalized switch
        {
            "tiem-nang" => "LEAD",
            "dang-hoat-dong" => "ACTIVE",
            "ngung-hoat-dong" => "INACTIVE",
            "da-luu-tru" => "ARCHIVED",
            _ => value?.Trim()
        };
        return Enum.TryParse(canonical, true, out status) && Enum.IsDefined(status);
    }

    private static bool TryParseResourceType(string? value, out InternalResourceType type)
    {
        var normalized = NormalizeAlias(value);
        var canonical = normalized switch
        {
            "de" or "de-thi" or "de-bai" => "EXAM",
            "tai-lieu" => "DOCUMENT",
            "ke-hoach" or "ban-ke-hoach" => "PLAN",
            "mau-thiet-ke" or "thiet-ke" => "DESIGN_SAMPLE",
            _ => value?.Trim()
        };
        return Enum.TryParse(canonical, true, out type) && Enum.IsDefined(type);
    }

    private static bool TryParseResourceStatus(string? value, out InternalResourceStatus status)
    {
        var normalized = NormalizeAlias(value);
        var canonical = normalized switch
        {
            "ban-nhap" => "DRAFT",
            "dang-su-dung" => "ACTIVE",
            "da-luu-tru" => "ARCHIVED",
            _ => value?.Trim()
        };
        return Enum.TryParse(canonical, true, out status) && Enum.IsDefined(status);
    }

    private static bool IsResourceTypeAllowed(BusinessSegment segment, InternalResourceType type) => segment switch
    {
        BusinessSegment.TECHNOLOGY_EDUCATION => type is InternalResourceType.EXAM or InternalResourceType.DOCUMENT,
        BusinessSegment.FASHION => type is InternalResourceType.PLAN or InternalResourceType.DESIGN_SAMPLE or InternalResourceType.DOCUMENT,
        _ => false
    };

    private static string ResourceTypeError(BusinessSegment segment) => segment == BusinessSegment.TECHNOLOGY_EDUCATION
        ? "Loại tài liệu của Công nghệ - Giáo dục chỉ gồm EXAM (đề) hoặc DOCUMENT (tài liệu)."
        : "Loại tài liệu của Thời trang chỉ gồm PLAN (kế hoạch), DESIGN_SAMPLE (mẫu thiết kế) hoặc DOCUMENT (tài liệu).";

    private static InternalCustomerDto MapCustomer(InternalCustomer x) => new(
        x.Id, x.CompanyId, x.BusinessUnitId!.Value, x.BusinessSegment.ToString(),
        InternalDataContract.SegmentName(x.BusinessSegment), x.Code, x.Name, x.ContactPerson,
        x.Email, x.Phone, x.Address, x.Source, x.Status.ToString(),
        InternalDataContract.CustomerStatusName(x.Status), x.Notes, x.CreatedAt, x.UpdatedAt);

    private static InternalResourceDto MapResource(InternalResource x) => new(
        x.Id, x.CompanyId, x.BusinessUnitId!.Value, x.BusinessSegment.ToString(),
        InternalDataContract.SegmentName(x.BusinessSegment), x.Code, x.Title, x.ResourceType.ToString(),
        InternalDataContract.ResourceTypeName(x.ResourceType), x.StorageUri, x.FileName, x.ContentType,
        x.FileSizeBytes, x.ChecksumSha256, x.Version, x.Status.ToString(),
        InternalDataContract.ResourceStatusName(x.Status), DeserializeTags(x.TagsCsv), x.MetadataJson,
        x.Notes, x.CreatedAt, x.UpdatedAt);

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static string NormalizeAlias(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
    private static string NormalizeTag(string value) => value.Trim().ToLowerInvariant();
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SerializeTags(IReadOnlyCollection<string>? tags)
    {
        var normalized = tags?.Select(NormalizeTag).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray() ?? [];
        return normalized.Length == 0 ? string.Empty : $"|{string.Join('|', normalized)}|";
    }

    private static string[] DeserializeTags(string tagsCsv) => string.IsNullOrWhiteSpace(tagsCsv)
        ? Array.Empty<string>()
        : tagsCsv.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed record InternalDataScope(Guid CompanyId, Guid BusinessUnitId);
}
