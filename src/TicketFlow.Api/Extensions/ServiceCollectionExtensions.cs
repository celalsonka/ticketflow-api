using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TicketFlow.Api.Common;
using TicketFlow.Application.Abstractions;
using TicketFlow.Application.Common;
using TicketFlow.Infrastructure.Persistence;
using TicketFlow.Infrastructure.Security;

namespace TicketFlow.Api.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
            context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<DocumentInfoTransformer>();
            options.AddOperationTransformer<AuthorizeOperationTransformer>();
        });

        services.AddJwtAuthentication();
        services.AddRateLimiting(configuration);

        services.AddOutputCache(options => options.AddPolicy(CacheTags.Events, policy => policy
            .Expire(TimeSpan.FromSeconds(15))
            .SetVaryByQuery("*")
            .Tag(CacheTags.Events)));

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Idempotent-Replayed", "X-Correlation-Id", "Retry-After")));

        return services;
    }

    private static void AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured from the bound options at runtime, so env vars / test overrides are honoured.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((options, jwtOptions, timeProvider) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TimeProvider = timeProvider;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.Secret),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    RoleClaimType = Policies.RoleClaim,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // Validate lifetimes against the injectable clock (deterministic tests with FakeTimeProvider).
                    LifetimeValidator = (notBefore, expires, _, parameters) =>
                    {
                        var now = timeProvider.GetUtcNow().UtcDateTime;
                        return (notBefore is null || notBefore.Value <= now + parameters.ClockSkew)
                            && (expires is null || expires.Value > now - parameters.ClockSkew);
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Organizer, policy => policy.RequireRole(nameof(Domain.Users.UserRole.Organizer), nameof(Domain.Users.UserRole.Admin)))
            .AddPolicy(Policies.Admin, policy => policy.RequireRole(nameof(Domain.Users.UserRole.Admin)));
    }

    private static void AddRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>().Bind(configuration.GetSection(RateLimitingOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Auth, http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(settings.AuthWindowSeconds),
                        QueueLimit = 0,
                    });
            });

            options.AddPolicy(RateLimitPolicies.Orders, http =>
            {
                var settings = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var partition = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? http.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetTokenBucketLimiter(partition, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = settings.OrdersTokenLimit,
                    TokensPerPeriod = settings.OrdersTokensPerMinute,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Rate limit exceeded. Please retry later.",
                    },
                });
            };
        });
    }
}
