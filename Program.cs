using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShippingPortal.Api.Data;
using ShippingPortal.Api.Models.Identity;
using ShippingPortal.Api.Services;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

var connectionString = builder.Configuration["CONNECTION_STRING"]
    ?? builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("No database connection string configured.");

builder.Services.AddDbContext<ShippingPortalDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 35)), mysqlOptions =>
        mysqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ShippingPortalDbContext>()
    .AddDefaultTokenProviders();

var jwtKey = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException("JWT_SECRET is not configured.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JWT_ISSUER"] ?? "ShippingPortal.Api",
            ValidAudience = builder.Configuration["JWT_AUDIENCE"] ?? "ShippingPortal.Client",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // Cryptographic validity alone isn't enough — this checks the
        // token's sessionVersion claim against the user's current value
        // in the database on every request, so "Revoke Sessions" takes
        // effect immediately rather than waiting for natural expiry.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // Uses the standard ASP.NET Core convention (matching
                // every other controller's own user-lookup), rather
                // than the raw JWT "sub" claim name, which the
                // framework automatically remaps to this by default.
                var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var tokenVersionClaim = context.Principal?.FindFirst("sessionVersion")?.Value;

                if (userId is null || tokenVersionClaim is null || !int.TryParse(tokenVersionClaim, out var tokenVersion))
                {
                    context.Fail("Invalid token.");
                    return;
                }

                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ShippingPortal.Api.Models.Identity.ApplicationUser>>();
                var user = await userManager.FindByIdAsync(userId);

                if (user is null || user.SessionVersion != tokenVersion)
                {
                    context.Fail("Session has been revoked.");
                }
            }
        };
    });

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<FxRateService>();
builder.Services.AddScoped<ClearanceScheduleService>();
builder.Services.AddScoped<DemurrageStorageService>();
builder.Services.AddScoped<ClearanceEstimatePdfService>();
builder.Services.AddScoped<PreClearanceReadinessService>();
builder.Services.AddScoped<BuAccessService>();
builder.Services.AddScoped<PoAdvancePaymentService>();
builder.Services.AddScoped<SettingsUploadService>();
builder.Services.AddScoped<SettingsExportService>();
builder.Services.AddScoped<SectionLockService>();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();

var allowedOrigins = (builder.Configuration["ALLOWED_ORIGINS"] ?? "http://localhost:4200")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// Login-specific limiter — per-account lockout already exists via
// Identity, but that alone doesn't stop an attacker rotating across
// many different email addresses. Partitioned by IP so one abusive
// client can't starve out everyone else.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("LoginPolicy", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Too many login attempts. Please wait a moment and try again.", token);
    };
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
}

await ClearanceSlaSeeder.SeedAsync(app.Services);
await SpcStorageTierSeeder.SeedAsync(app.Services);

// Gated behind auth — the full API surface (every route, parameter,
// and DTO shape) shouldn't be handed out to anyone who asks.
app.MapOpenApi().RequireAuthorization();

app.UseCors("Frontend");
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
