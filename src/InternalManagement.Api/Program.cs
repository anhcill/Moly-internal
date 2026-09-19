using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;
using InternalManagement.Application;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Infrastructure;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Api.Middlewares;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction())
{
    var jwtSecret = builder.Configuration["JwtSettings:Secret"];
    if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    {
        throw new InvalidOperationException(
            "Production requires JwtSettings:Secret with at least 32 random characters via environment/secret store.");
    }
}

// Add Application and Infrastructure services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Add Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

var loginRateLimit = Math.Clamp(builder.Configuration.GetValue<int?>("Security:Authentication:LoginRateLimitPerMinute") ?? 5, 1, 60);
var refreshRateLimit = Math.Clamp(builder.Configuration.GetValue<int?>("Security:Authentication:RefreshRateLimitPerMinute") ?? 10, 1, 120);
var isTestingEnvironment = builder.Environment.IsEnvironment("Testing");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth-login", context =>
    {
        if (isTestingEnvironment) return RateLimitPartition.GetNoLimiter("testing");
        return RateLimitPartition.GetFixedWindowLimiter(
            $"login:{context.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = loginRateLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.AddPolicy("auth-refresh", context =>
    {
        if (isTestingEnvironment) return RateLimitPartition.GetNoLimiter("testing");
        return RateLimitPartition.GetFixedWindowLimiter(
            $"refresh:{context.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = refreshRateLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

// Add Health Checks
if (!builder.Environment.IsEnvironment("Testing"))
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            $"{(builder.Environment.IsProduction() ? "Production" : "Application")} requires ConnectionStrings:DefaultConnection via environment/secret store.");
    }

    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString, name: "PostgreSQL", tags: new[] { "ready", "db" });
}
else
{
    builder.Services.AddHealthChecks();
}

// Add OpenAPI / Swagger with JWT Security Definition
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MOLI Internal Management API",
        Version = "v1",
        Description = "ASP.NET Core Web API cho hệ thống quản trị MOLI (EdTech, CSCA, Interview, HR, Fashion, Finance)."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập JWT Access Token vào ô bên dưới (không cần gõ tiền tố 'Bearer '):"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        if (builder.Environment.IsProduction())
        {
            var allowedOrigins = builder.Configuration
                .GetSection("Cors:AllowedOrigins")
                .GetChildren()
                .Select(x => x.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray();

            if (allowedOrigins.Length == 0)
            {
                policy.SetIsOriginAllowed(_ => false);
            }
            else
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            }
        }
        else
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// In production the schema must be ready before serving traffic. In development,
// automatic migration is opt-in so a remote/slow database never blocks the edit
// and Hot Reload loop. Apply schema changes explicitly with `dotnet ef database update`.
var applyMigrationsOnStartup = builder.Configuration.GetValue<bool?>("DatabaseStartup:ApplyMigrationsOnStartup")
    ?? app.Environment.IsProduction();
var requireDatabaseOnStartup = builder.Configuration.GetValue<bool?>("DatabaseStartup:RequireConnectionOnStartup")
    ?? app.Environment.IsProduction();

// Synthetic/demo data is only seeded by the Testing host or when explicitly opted
// in together with database startup migrations.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var dbContext = services.GetRequiredService<ApplicationDbContext>();

    try
    {
        if (app.Environment.IsEnvironment("Testing"))
        {
            await dbContext.Database.EnsureCreatedAsync();
            var seeder = services.GetRequiredService<IDatabaseSeeder>();
            await seeder.SeedAsync();
        }
        else if (dbContext.Database.IsRelational() && applyMigrationsOnStartup)
        {
            var canConnect = await CanConnectWithRetryAsync(
                dbContext,
                logger,
                app.Lifetime.ApplicationStopping);
            if (canConnect)
            {
                await dbContext.Database.MigrateAsync();
                if (!app.Environment.IsProduction() &&
                    app.Configuration.GetValue<bool>("DatabaseSeed:EnableDemoData"))
                {
                    var seeder = services.GetRequiredService<IDatabaseSeeder>();
                    await seeder.SeedAsync();
                }
                else
                {
                    logger.LogInformation("Automatic demo-data seeding is disabled. Use an explicit bootstrap/test operation when needed.");
                }
            }
            else if (requireDatabaseOnStartup)
            {
                const string message = "Không thể kết nối PostgreSQL khi khởi động API.";
                logger.LogCritical(message);
                throw new InvalidOperationException(message);
            }
            else
            {
                logger.LogWarning(
                    "Không thể kết nối PostgreSQL khi khởi động API; bỏ qua migration vì DatabaseStartup:RequireConnectionOnStartup=false.");
            }
        }
        else if (dbContext.Database.IsRelational())
        {
            logger.LogInformation(
                "Automatic database migration is disabled for {Environment}. Use 'dotnet ef database update' after creating a migration.",
                app.Environment.EnvironmentName);
        }
    }
    catch (Exception ex)
    {
        if (app.Environment.IsEnvironment("Testing"))
        {
            throw;
        }

        if (app.Environment.IsProduction() || app.Environment.IsDevelopment())
        {
            logger.LogCritical(ex, "Database migration/seed failed; refusing to start with an invalid schema.");
            throw;
        }

        logger.LogWarning(ex, "Database migration/seed skipped or failed during startup: {Message}", ex.Message);
    }
}

// Global Exception Handler
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// Configure HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MOLI Internal Management API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors("DefaultCorsPolicy");
app.UseRouting();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});

// Authentication & Authorization
app.UseAuthentication();
app.UseMiddleware<TenantScopeMiddleware>();
app.UseAuthorization();

// Audit Logging Middleware
app.UseMiddleware<AuditLogMiddleware>();

// Health Check Endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            duration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapControllers();

app.Run();

static async Task<bool> CanConnectWithRetryAsync(
    ApplicationDbContext dbContext,
    ILogger logger,
    CancellationToken cancellationToken)
{
    const int maxAttempts = 3;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            if (await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return true;
            }

            logger.LogWarning(
                "PostgreSQL connection attempt {Attempt}/{MaxAttempts} returned no connection.",
                attempt,
                maxAttempts);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "PostgreSQL connection attempt {Attempt}/{MaxAttempts} failed.",
                attempt,
                maxAttempts);
        }

        if (attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
        }
    }

    return false;
}

public partial class Program { }
