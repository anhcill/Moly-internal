using Microsoft.Extensions.DependencyInjection;

namespace InternalManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application use cases / handlers / validators
        return services;
    }
}
