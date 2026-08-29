using api.Dal;
using api.Dal.Interface;
using api.Filters;
using api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var secureKey = builder.Configuration["JWT:SecureKey"];
if (string.IsNullOrEmpty(secureKey))
    throw new InvalidOperationException("JWT:SecureKey is missing in configuration.");

var jwtIssuer = builder.Configuration["JWT:Issuer"];
var jwtAudience = builder.Configuration["JWT:Audience"];
if (string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience))
    throw new InvalidOperationException("JWT:Issuer and JWT:Audience are missing in configuration.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => {
        var Key = Encoding.UTF8.GetBytes(secureKey);
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Key)
        };
    });

// Device-communication endpoints authenticate by apiId/apiKey (or the short-lived apiAuth
// session token), not a user JWT - see api.Security.DeviceAuth.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(DeviceAuth.ApiKeyPolicy, p => p.AddRequirements(new DeviceApiKeyRequirement()));
    options.AddPolicy(DeviceAuth.SessionPolicy, p => p.AddRequirements(new DeviceSessionRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, DeviceApiKeyHandler>();
builder.Services.AddScoped<IAuthorizationHandler, DeviceSessionHandler>();

builder.Services.AddScoped<IRepository, EfRepository>();
builder.Services.AddScoped<ICache, CacheRepository>();
builder.Services.AddScoped<DbExceptionFilter>();

builder.Services.AddControllers(options => options.Filters.AddService<DbExceptionFilter>());

// Rate limiting - all policies are fixed-window, partitioned by client IP, reject with 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static RateLimitPartition<string> IpFixedWindow(HttpContext httpContext, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });

    // Human interactive login/registration - strict, this is the credential-stuffing target.
    options.AddPolicy("login", httpContext => IpFixedWindow(httpContext, 5));

    // Device onboarding/auth/config poll: ~2 req/min per device; 20/min/IP covers ~10 devices behind one NAT plus retries.
    options.AddPolicy("device-auth", httpContext => IpFixedWindow(httpContext, 20));

    // Device telemetry push: ~1/min per device; higher ceiling for many devices behind one NAT and catch-up bursts.
    options.AddPolicy("device-data", httpContext => IpFixedWindow(httpContext, 60));
});

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddLogging();

// HTTPS enforcement is opt-out via Security:EnforceHttps=false (default true), e.g. while firmware is still on http://.
bool enforceHttps = !bool.TryParse(builder.Configuration["Security:EnforceHttps"], out var eh) || eh;

// HSTS only when enforcing HTTPS and outside Development (see pipeline below).
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

builder.Services.AddSwaggerGen(option =>
{
    option.SwaggerDoc("v1",
        new OpenApiInfo { Title = "Agrumy Web API", Version = "v1" });

    option.AddSecurityDefinition("Bearer",
        new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Please enter valid JWT",
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            BearerFormat = "JWT",
            Scheme = "Bearer"
        });

    option.AddSecurityRequirement(
        new OpenApiSecurityRequirement
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
                new List<string>()
            }
        });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// UseHsts only outside Development so local HTTP dev without a cert still works; UseHttpsRedirection is a no-op in dev with no https port.
if (enforceHttps)
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Run the DB check at startup, not lazily on first request, so a bad connection string shows in deploy logs; Startup:FailFastOnDbCheck controls stop-vs-warn.
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var repository = scope.ServiceProvider.GetRequiredService<IRepository>();
    bool failFastOnDbCheck = bool.TryParse(builder.Configuration["Startup:FailFastOnDbCheck"], out var failFast) && failFast;

    try
    {
        if (await repository.TestConnectionAsync())
        {
            startupLogger.LogInformation("Startup DB check: database connection OK.");
            await repository.EnsureSchemaAsync();
            startupLogger.LogInformation("Startup DB check: schema verified/provisioned.");
        }
        else
        {
            const string message = "Startup DB check: could not open a database connection.";
            if (failFastOnDbCheck)
                throw new InvalidOperationException(message);
            startupLogger.LogError(message);
        }
    }
    catch (Exception ex) when (!failFastOnDbCheck)
    {
        startupLogger.LogError(ex, "Startup DB check failed; continuing because Startup:FailFastOnDbCheck is false.");
    }
}

app.Run();
