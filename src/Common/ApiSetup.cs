using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Serilog;
using Serilog.Events;

namespace Test3.Common;

public static class ApiSetup
{
    public static string Required(IConfiguration config, string key) =>
        !string.IsNullOrWhiteSpace(config[key]) ? config[key]! : throw new InvalidOperationException($"Configure {key} before starting the service.");

    public static void AddApi(this WebApplicationBuilder builder, string service, string database)
    {
        builder.Host.UseSerilog((_, logging) => logging.MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogEventLevel.Fatal)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}")
            .WriteTo.File(Path.Combine(builder.Environment.ContentRootPath, "logs", service + "-.log"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true, retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:O} [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}"));

        builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = context =>
            {
                var problem = new ValidationProblemDetails(context.ModelState)
                { Status = 400, Title = "Invalid request", Instance = context.HttpContext.Request.Path };
                problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
                return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
            });
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        var key = Required(builder.Configuration, "Jwt:Key");
        if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes.");
        var issuer = Required(builder.Configuration, "Jwt:Issuer");
        var audience = Required(builder.Configuration, "Jwt:Audience");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero,
                NameClaimType = "sub", RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var user = context.Principal!;
                    if (!Guid.TryParse(user.FindFirstValue("sub"), out var id) || id == Guid.Empty ||
                        !(user.IsInRole("ADMIN") || user.IsInRole("USER")))
                        context.Fail("Invalid identity.");
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorization(options => options.FallbackPolicy =
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole("ADMIN", "USER").Build());
        var connectionString = Required(builder.Configuration, "ConnectionStrings:" + database);
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
    }

    public static void UseApi(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.TraceIdentifier = Guid.TryParse(context.Request.Headers["X-Correlation-ID"], out var id)
                ? id.ToString("N") : Guid.NewGuid().ToString("N");
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
                return Task.CompletedTask;
            });
            using var scope = app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = context.TraceIdentifier });
            await next(context);
            app.Logger.LogInformation("HTTP {Method} {Path} returned {StatusCode}", context.Request.Method, context.Request.Path, context.Response.StatusCode);
        });
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
    }
}
