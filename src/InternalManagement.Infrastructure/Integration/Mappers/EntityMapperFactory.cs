using InternalManagement.Application.Features.Integration.Interfaces;

namespace InternalManagement.Infrastructure.Integration.Mappers;

/// <summary>
/// Registry resolve IEntityMapper theo entity type từ DI.
/// </summary>
public sealed class EntityMapperFactory : IEntityMapperFactory
{
    private readonly Dictionary<string, IEntityMapper> _mappers;

    public EntityMapperFactory(IEnumerable<IEntityMapper> mappers)
    {
        _mappers = mappers.ToDictionary(
            m => m.EntityType,
            m => m,
            StringComparer.OrdinalIgnoreCase);
    }

    public IEntityMapper GetMapper(string entityType)
    {
        if (_mappers.TryGetValue(entityType, out var mapper))
            return mapper;

        throw new InvalidOperationException(
            $"Không tìm thấy entity mapper cho '{entityType}'. " +
            $"Các mapper khả dụng: {string.Join(", ", _mappers.Keys)}");
    }

    public bool HasMapper(string entityType)
        => _mappers.ContainsKey(entityType);
}
