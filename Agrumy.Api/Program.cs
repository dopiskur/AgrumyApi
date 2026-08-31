using api.Dal;
using api.Dal.Interface;
using api.Filters;
using api.Notifications;
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

// Roadmap #74: one scoped EfRepository instance, exposed as the full IRepository (controllers)
// and forwarded to every per-domain facet, so a narrow consumer (DbExceptionFilter,
// DeviceApiKeyHandler, future background workers) can inject just the interface it needs.
builder.Services.AddScoped<EfRepository>();
builder.Services.AddScoped<IRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<ISystemRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<IServerConfigRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<IUserRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<ITenantRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<IRefreshTokenRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<IDeviceRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<ISensorDataRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<ICache, CacheRepository>();
builder.Services.AddScoped<DbExceptionFilter>();

// Alert delivery (roadmap #6). Email is live; the FCM push channel is registered but stays
// skipped until the Android app registers device tokens - see FcmPushNotificationChannel.
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, FcmPushNotificationChannel>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

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

// Roadmap #69: JwtTokenProvider is static (no DI reach) - hand it a logger once so token
// rejections land in the normal log pipeline instead of vanishing.
JwtTokenProvider.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JwtTokenProvider));

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

            // ServerConfig:Reload (roadmap #10) - force the DB's hysteresis defaults back to
            // appsettings.json. Off by default; see Config.serverConfigReload for why. Nested
            // inside the DB-is-reachable branch so a Reload=true install with the DB down still
            // falls through to the same failFastOnDbCheck handling below instead of throwing past it.
            if (api.Config.serverConfigReload)
            {
                await repository.ServerConfigReloadFromAppSettingsAsync(1);
                startupLogger.LogInformation("ServerConfig:Reload was true - serverConfig hysteresis fields overwritten from appsettings.json.");
            }
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
