using api;
using api.BackgroundWorkers;
using api.Commands;
using api.Firmware;
using api.Dal;
using api.Dal.Interface;
using api.Filters;
using api.Notifications;
using api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Roadmap #104: must run before anything touches the static JwtTokenProvider (whose Config.
// secureKey/jwtIssuer/jwtAudience it populates) - this host's real builder.Configuration, not
// Config's old self-built ConfigurationBuilder from Directory.GetCurrentDirectory().
Config.Init(builder.Configuration);

// Roadmap #101/#104: one AgrumySettings snapshot per process, bound from the real host
// IConfiguration - see AgrumySettings.Bind for exactly which keys/env vars feed it.
builder.Services.AddSingleton(Options.Create(AgrumySettings.Bind(builder.Configuration)));

// Roadmap #101: real scoped lifetime - one AgrumyDbContext per HTTP request/background-worker
// tick (AddScoped, matching EfRepository's own scoped registration below), not a new one per
// repository method call. Provider/connection string come from AgrumySettings, not appsettings
// directly, so the AGRUMY_DB_PROVIDER env-var override (AgrumySettings.Bind) still applies.
builder.Services.AddScoped(sp =>
{
    AgrumySettings settings = sp.GetRequiredService<IOptions<AgrumySettings>>().Value;
    string connectionString = settings.DefaultConnection
        ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.");
    return new AgrumyDbContext(DbOptionsFactory.Build(DbProviderKindParser.Parse(settings.DatabaseProvider), connectionString));
});

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
builder.Services.AddScoped<IDeviceUnitRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<ICommandRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<IFirmwareRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<ISensorDataRepository>(sp => sp.GetRequiredService<EfRepository>());
// Roadmap #72: in-process today (same practical behaviour as the old MemoryCache - lost on
// restart, not shared across instances), but CacheRepository talks to IDistributedCache, so a
// real scale-out backend is a swap of this one line (e.g. AddStackExchangeRedisCache(...)), not
// an application code change.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddScoped<ICache, CacheRepository>();
builder.Services.AddScoped<DbExceptionFilter>();

// Alert delivery (roadmap #6). Email is live; the FCM push channel is registered but stays
// skipped until the Android app registers device tokens - see FcmPushNotificationChannel.
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, FcmPushNotificationChannel>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

// Roadmap #40 (infra) + #6 (offline alert type). Scoped, not singleton - it resolves scoped
// repositories/dispatcher itself and PeriodicBackgroundService creates a fresh DI scope per tick.
builder.Services.AddScoped<OfflineAlertEvaluator>();
builder.Services.AddHostedService<OfflineAlertBackgroundService>();

// Roadmap #34: no background worker - expiry is lazy (CommandQueueService.GetPendingCommandAsync
// marks a stale row Expired the moment it's next looked at), so this is a plain scoped service,
// not an IHostedService registration like OfflineAlertEvaluator above.
builder.Services.AddScoped<CommandQueueService>();

// Roadmap #126: on-demand background jobs (Optimize/Purge Old Data) - singleton queue so it
// outlives any one request's DI scope, consumed one at a time by BackgroundJobRunner.
builder.Services.AddSingleton<BackgroundJobQueue>();
builder.Services.AddHostedService<BackgroundJobRunner>();

// Roadmap #94: firmware catalog. One named HttpClient for GitHub/Custom-repository reads and
// .bin downloads - the default handler follows the 302 a GitHub release asset answers with.
builder.Services.AddHttpClient(HttpFirmwareFetcher.ClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(5); // a full "pull from GitHub" streams several MB per file
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Agrumy.Api/1.0 (+https://github.com/dopiskur/AgrumyService)");
});
builder.Services.AddSingleton<IFirmwareFetcher, HttpFirmwareFetcher>();
builder.Services.AddSingleton<FirmwareStorage>();
builder.Services.AddScoped<FirmwareCatalogService>();

builder.Services.AddControllers(options => options.Filters.AddService<DbExceptionFilter>());

// Roadmap #84: the rate limiter below partitions by Connection.RemoteIpAddress - behind a reverse
// proxy (roadmap #30) that's always the proxy's own IP, so every real client shares one bucket and
// rate limiting is effectively disabled. ForwardedHeadersMiddleware rewrites RemoteIpAddress from
// X-Forwarded-For before the limiter (or anything else) sees it, but ONLY for a request whose
// immediate peer is in KnownProxies - left unconfigured, ForwardedHeadersOptions' own default
// trusts loopback only, which is correct for a same-box proxy and, critically, is NOT a wildcard:
// trusting an arbitrary peer would let any client spoof X-Forwarded-For and both bypass rate
// limiting and forge its apparent IP everywhere else that reads it. A remote/containerized proxy
// must list its real address(es) explicitly via Security:KnownProxies (comma-separated) in
// appsettings.json.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    string? configuredProxies = builder.Configuration["Security:KnownProxies"];
    if (!string.IsNullOrWhiteSpace(configuredProxies))
    {
        foreach (string proxy in configuredProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPAddress.TryParse(proxy, out IPAddress? ip))
            {
                options.KnownProxies.Add(ip);
            }
        }
    }
});

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

// Must run before anything that reads Connection.RemoteIpAddress or Request.Scheme - the rate
// limiter (roadmap #84) below, but also UseHttpsRedirection/UseHsts further down.
app.UseForwardedHeaders();

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
            // appsettings.json. Off by default; see AgrumySettings.ServerConfigReload for why.
            // Nested inside the DB-is-reachable branch so a Reload=true install with the DB down
            // still falls through to the same failFastOnDbCheck handling below instead of throwing past it.
            if (scope.ServiceProvider.GetRequiredService<IOptions<AgrumySettings>>().Value.ServerConfigReload)
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
