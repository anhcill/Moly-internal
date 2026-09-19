using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Auth.Services;
using InternalManagement.Application.Features.EdTech.Services;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.CscaInterview.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Application.Features.Fashion.Services;
using InternalManagement.Application.Features.InternalData.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Integration.Connectors;
using InternalManagement.Infrastructure.Integration.Mappers;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Security;
using InternalManagement.Infrastructure.Services;
using InternalManagement.Infrastructure.Storage;

namespace InternalManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var isInMemory = configuration.GetValue<bool>("UseInMemoryDatabase") ||
                         configuration["DatabaseProvider"] == "InMemory" ||
                         Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing";
        var isProduction = IsProduction(configuration);
        if (isInMemory && isProduction)
        {
            throw new InvalidOperationException("Production không được phép chạy bằng InMemory database. Hãy cấu hình PostgreSQL thật qua ConnectionStrings:DefaultConnection.");
        }

        if (isInMemory)
        {
            var inMemoryRoot = new InMemoryDatabaseRoot();
            services.AddSingleton(inMemoryRoot);
            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                var root = sp.GetRequiredService<InMemoryDatabaseRoot>();
                options.UseInMemoryDatabase("MoliTestInMemoryDb", root);
                options.UseSnakeCaseNamingConvention();
                options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            });
        }
        else
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"{(isProduction ? "Production" : "Application")} requires ConnectionStrings:DefaultConnection via environment/secret store.");
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                });
                options.UseSnakeCaseNamingConvention();
                options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            });
        }

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Security & Auth services
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDatabaseSeeder, DatabaseSeeder>();

        // Fine-grained RBAC Authorization
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // Integration — Connectors
        // Mock/Null connectors are test-only. Production must fail clearly when a
        // real external connector is not configured instead of writing synthetic data.
        if (isInMemory)
        {
            services.AddScoped<IExternalConnector, NullConnector>();
            services.AddScoped<IExternalConnector, WebsiteEdtechConnector>();
        }
        services.AddHttpClient<CscaMoliStudioConnector>();
        services.AddHttpClient<CscaInterviewConnector>();
        services.AddHttpClient<ICscaCourseLmsClient, CscaCourseLmsClient>();
        services.AddScoped<IExternalConnector>(sp => sp.GetRequiredService<CscaMoliStudioConnector>());
        services.AddScoped<IExternalConnector>(sp => sp.GetRequiredService<CscaInterviewConnector>());
        services.AddScoped<IConnectorFactory, ConnectorFactory>();

        // Integration — Entity Mappers
        services.AddScoped<IEntityMapper, CourseMapper>();
        services.AddScoped<IEntityMapper, CustomerMapper>();
        services.AddScoped<IEntityMapper, PaymentMapper>();
        services.AddScoped<IEntityMapper, SubscriptionMapper>();
        services.AddScoped<IEntityMapper, QuestionMapper>();
        services.AddScoped<IEntityMapper, InterviewCustomerMapper>();
        services.AddScoped<IEntityMapperFactory, EntityMapperFactory>();

        // Shared data backbone: canonical parties and operational document registry.
        services.AddScoped<IPartyResolver, PartyResolver>();
        services.AddScoped<IBusinessDocumentRegistry, BusinessDocumentRegistry>();
        services.AddScoped<IMasterDataBackfillService, MasterDataBackfillService>();

        // Integration — Sync Engine & Services
        services.AddScoped<ISyncEngine, SyncEngine>();
        services.AddScoped<IWebhookProcessor, WebhookProcessor>();
        services.AddScoped<IDeadLetterService, DeadLetterService>();
        services.AddScoped<ILmsAccessLifecycleService, LmsAccessLifecycleService>();
        services.AddScoped<ILmsOutboxDispatcher, LmsOutboxDispatcher>();
        services.AddScoped<ILmsIntegrationOperationsService, LmsIntegrationOperationsService>();
        services.AddScoped<IApplicationOrchestrationService, ApplicationOrchestrationService>();
        if (!isInMemory)
            services.AddHostedService<LmsOutboxWorker>();

        // Module Services
        services.AddScoped<IEdTechService, EdTechService>();
        services.AddScoped<ICscaService, CscaService>();
        services.AddHttpClient<ICscaOnlineService, CscaOnlineService>();
        services.AddScoped<IInterviewService, InterviewService>();
        services.AddScoped<IProfitAllocationService, ProfitAllocationService>();
        services.AddScoped<IFinancePostingService, FinancePostingService>();
        services.AddScoped<IFinanceLedgerService, FinanceLedgerService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IPayrollService, PayrollService>();
        services.AddScoped<IPayrollExportService, PayrollExcelExportService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IManufacturingService, ManufacturingService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IOrderCostingService, OrderCostingService>();
        services.AddScoped<IInternalDataService, InternalDataService>();
        services.Configure<CloudinarySettings>(configuration.GetSection("Cloudinary"));
        services.AddHttpClient<IFileStorageService, CloudinaryFileStorageService>();

        // JWT Bearer Authentication
        var isProductionEnvironment = IsProduction(configuration);
        var secret = configuration["JwtSettings:Secret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException(
                $"{(isProductionEnvironment ? "Production" : "Application")} requires JwtSettings:Secret via environment/secret store.");
        }

        if (secret.Length < 32)
        {
            throw new InvalidOperationException("JwtSettings:Secret must contain at least 32 characters.");
        }

        var issuer = configuration["JwtSettings:Issuer"] ?? "MoliBackendApi";
        var audience = configuration["JwtSettings:Audience"] ?? "MoliClients";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = isProductionEnvironment;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization();

        return services;
    }

    private static bool IsProduction(IConfiguration configuration)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
    }
}
