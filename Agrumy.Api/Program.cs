using api;
using Asp.Versioning;
using api.BackgroundWorkers;
using api.Commands;
using api.Diagnostics;
using api.Firmware;
using api.Dal;
using api.Dal.Interface;
using api.Filters;
using api.Notifications;
using api.Security;
using api.Weather;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Must run before anything touches the static JwtTokenProvider, which this populates.
Config.Init(builder.Configuration);

AgrumySettings settingsForBootCheck = AgrumySettings.Bind(builder.Configuration);
builder.Services.AddSingleton(Options.Create(settingsForBootCheck));

// A first boot with no DB connection string routes to the minimal setup wizard instead of the rest of this file, until an admin supplies one - see Agrumy.Api/Setup/SetupWizard.cs.
if (string.IsNullOrWhiteSpace(settingsForBootCheck.DefaultConnection))
{
    api.Setup.SetupWizard.ConfigureServices(builder);
    var wizardApp = builder.Build();
    api.Setup.SetupWizard.LogSetupToken(wizardApp.Services.GetRequiredService<ILogger<Program>>());
    api.Setup.SetupWizard.MapEndpoints(wizardApp);
    await wizardApp.RunAsync();
    return;
}

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
        // Same factory JwtTokenProvider.ValidateToken uses - see its BuildValidationParameters for why.
        o.TokenValidationParameters = JwtTokenProvider.BuildValidationParameters(secureKey, jwtIssuer, jwtAudience);
        // A JWT is self-validating and cannot be un-issued before its own expiry, so a password change or Enabled->false only takes effect immediately via this extra per-request check.
        o.Events = new JwtBearerEvents { OnTokenValidated = TokenRevocationValidator.ValidateAsync };
    });

// Device-communication endpoints authenticate by apiId/apiKey (or the short-lived apiAuth session token), not a user JWT - see api.Security.DeviceAuth.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(DeviceAuth.ApiKeyPolicy, p => p.AddRequirements(new DeviceApiKeyRequirement()));
    options.AddPolicy(DeviceAuth.SessionPolicy, p => p.AddRequirements(new DeviceSessionRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, DeviceApiKeyHandler>();
builder.Services.AddScoped<IAuthorizationHandler, DeviceSessionHandler>();

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
builder.Services.AddScoped<IAuditLogRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<IGatewayRepository>(sp => sp.GetRequiredService<EfRepository>());
builder.Services.AddScoped<IDiscoveryRepository>(sp => sp.GetRequiredService<EfRepository>());

// Cache:Redis:ConnectionString switches to Redis; unset/empty keeps the in-process default.
string? redisConnectionString = builder.Configuration["Cache:Redis:ConnectionString"];
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnectionString);
}
builder.Services.AddScoped<ICache, CacheRepository>();
builder.Services.AddScoped<DbExceptionFilter>();

// FCM push channel is registered but stays inert until the Android app registers device tokens - see FcmPushNotificationChannel.
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.AddScoped<INotificationChannel, EmailNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, FcmPushNotificationChannel>();
builder.Services.AddScoped<INotificationChannel, WebhookNotificationChannel>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

// AllowAutoRedirect=false: a redirect response is treated as a delivery failure rather than followed, same SsrfGuard-bypass concern as HttpFirmwareFetcher below, simpler to just refuse it here.
builder.Services.AddHttpClient(WebhookNotificationChannel.ClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Agrumy.Api/1.0 (+https://github.com/dopiskur/AgrumyService)");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Scoped, not singleton: it resolves scoped repositories/dispatcher itself, and PeriodicBackgroundService creates a fresh DI scope per tick.
builder.Services.AddScoped<OfflineAlertEvaluator>();
builder.Services.AddHostedService<OfflineAlertBackgroundService>();

builder.Services.AddScoped<LowBatteryAlertEvaluator>();
builder.Services.AddHostedService<LowBatteryAlertBackgroundService>();

builder.Services.AddScoped<TankRefillAlertEvaluator>();
builder.Services.AddHostedService<TankRefillAlertBackgroundService>();

// Roadmap #212: server-side evaluator for Notification-action rules - a Relay-action rule's fold runs on-device instead.
builder.Services.AddScoped<RuleNotificationEvaluator>();
builder.Services.AddHostedService<RuleNotificationBackgroundService>();

// MariaDB retention runs here; PostgreSQL/TimescaleDB installs use EfRepository.ApplyRetentionPolicyAsync (a native TimescaleDB policy) instead.
builder.Services.AddScoped<SensorDataRetentionEvaluator>();
builder.Services.AddHostedService<SensorDataRetentionBackgroundService>();

builder.Services.AddScoped<DeviceCommandRetentionEvaluator>();
builder.Services.AddHostedService<DeviceCommandRetentionBackgroundService>();

builder.Services.AddHttpClient<IWeatherForecastClient, OpenWeatherMapClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddScoped<WeatherEvaluator>();
builder.Services.AddHostedService<WeatherBackgroundService>();

builder.Services.AddScoped<api.Commands.IMqttCommandPublisher, api.Commands.MqttCommandPublisher>();
builder.Services.AddScoped<CommandQueueService>();
builder.Services.AddScoped<api.Devices.DeviceConfigBuilder>();
builder.Services.AddScoped<api.Migration.TenantExportService>();
builder.Services.AddScoped<api.Migration.TenantImportService>();

// Singleton so it outlives any one request's DI scope; BackgroundJobRunner consumes it one job at a time.
builder.Services.AddSingleton<BackgroundJobQueue>();
builder.Services.AddHostedService<BackgroundJobRunner>();

builder.Services.AddHttpClient(HttpFirmwareFetcher.ClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(5); // a full "pull from GitHub" streams several MB per file
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Agrumy.Api/1.0 (+https://github.com/dopiskur/AgrumyService)");
})
// AllowAutoRedirect=false: HttpFirmwareFetcher follows redirects itself so SsrfGuard re-validates every hop.
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IFirmwareFetcher, HttpFirmwareFetcher>();
builder.Services.AddSingleton<FirmwareStorage>();
builder.Services.AddScoped<FirmwareCatalogService>();

builder.Services.AddScoped<FirmwareCatalogRefreshEvaluator>();
builder.Services.AddHostedService<FirmwareCatalogRefreshBackgroundService>();

// BaseAddress is the server's OWN public address - the virtual-device runner calls itself over the real wire protocol, same as a real device would target ServicePoint. Falls back to the documented local dev default when WebView:ApiService isn't configured.
builder.Services.AddHttpClient(VirtualDeviceRunnerBackgroundService.HttpClientName, (sp, client) =>
{
    string apiService = sp.GetRequiredService<IOptions<AgrumySettings>>().Value.ApiService is { Length: > 0 } configured
        ? configured
        : "http://localhost:5000";
    client.BaseAddress = new Uri(apiService.Contains("://") ? apiService : $"https://{apiService}");
});
builder.Services.AddSingleton<api.Simulation.SimulatedSensorGenerator>();
builder.Services.AddHostedService<VirtualDeviceRunnerBackgroundService>();

// AgrumyMetrics is a singleton because its ConcurrentDictionary aggregate must span every request/scope, unlike the AddScoped registrations above.
builder.Services.AddSingleton<AgrumyMetrics>();
builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<CacheHealthCheck>("cache");

// Listens on the same "Agrumy.Api" Meter the JSON /metrics endpoint already reads, so Prometheus/Grafana get the identical counters with no separate instrumentation.
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(AgrumyMetrics.MeterName)
        .AddPrometheusExporter());

builder.Services.AddControllers(options => options.Filters.AddService<DbExceptionFilter>());

// AssumeDefaultVersionWhenUnspecified keeps every existing caller (device firmware, Agrumy.Web's Refit client) working unversioned on 1.0; a future breaking change adds its own [ApiVersion("2.0")] controller instead of altering this one.
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new QueryStringApiVersionReader("api-version"),
        new HeaderApiVersionReader("X-Api-Version"));
})
.AddMvc()
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
})
.AddOpenApi(options =>
{
    options.Document.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Agrumy Web API";

        var components = document.Components ??= new OpenApiComponents();
        components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Please enter valid JWT"
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });

        return Task.CompletedTask;
    });
});

// KnownProxies must list only real proxy IPs (Security:KnownProxies) - trusting an arbitrary peer would let any client spoof X-Forwarded-For to bypass rate limiting and forge its apparent IP.
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

    // A fixed-window limiter's RetryAfter metadata is exact, so a caller that honors it - notably Agrumy.Gateway, forwarding it to devices as a "Wait" signal - gets a real number instead of guessing a backoff.
    options.OnRejected = (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }
        return ValueTask.CompletedTask;
    };

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

// ClearProviders first, or JSON console just stacks on top of the default plain-text provider CreateBuilder registers.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
}
else
{
    builder.Logging.AddJsonConsole(o =>
    {
        o.UseUtcTimestamp = true;
        o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        o.IncludeScopes = true;
    });
}
builder.Logging.AddDebug();

// HTTPS enforcement is opt-out via Security:EnforceHttps=false (default true), e.g. while firmware is still on http://.
bool enforceHttps = !bool.TryParse(builder.Configuration["Security:EnforceHttps"], out var eh) || eh;

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

// JwtTokenProvider is static (no DI reach) - hand it a logger once so token rejections land in the normal log pipeline instead of vanishing.
JwtTokenProvider.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JwtTokenProvider));

// Must run before anything that reads Connection.RemoteIpAddress or Request.Scheme - the rate limiter below, but also UseHttpsRedirection/UseHsts further down.
app.UseForwardedHeaders();

// UseSwaggerUI just renders the Microsoft.AspNetCore.OpenApi-generated document; WithDocumentPerVersion generates one per discovered API version instead of a single hardcoded "v1".
app.MapOpenApi().WithDocumentPerVersion();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Agrumy Web API v1"));

// UseHsts only outside Development so local HTTP dev without a cert still works; UseHttpsRedirection is a no-op in dev with no https port.
if (enforceHttps)
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseRouting();

// After UseRouting (needs the matched route pattern) but before rate limiting/auth/the endpoint itself, so recorded duration covers the whole request.
app.UseMiddleware<RequestMetricsMiddleware>();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Unauthenticated on purpose: restart/deploy probes and external uptime monitors need to reach this without a JWT; it exposes only up/down + which dependency, nothing sensitive.
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthCheckResponseWriter.WriteResponse });

app.MapGet("/metrics", (AgrumyMetrics metrics) => Results.Json(metrics.GetSnapshot()))
    .RequireAuthorization(policy => policy.RequireRole(RoleNames.MetricsReaders));

// Same JWT policy as the JSON endpoint above; point Prometheus's scrape config at this path with that bearer token (no separate secret to manage).
app.MapPrometheusScrapingEndpoint("/metrics/prometheus")
    .RequireAuthorization(policy => policy.RequireRole(RoleNames.MetricsReaders));

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

namespace api
{
    /// Marker type for Agrumy.Api.Tests' WebApplicationFactory - a dedicated type instead of the
    /// implicit top-level Program avoids a CS0433 clash with Agrumy.Web's own Program once both
    /// assemblies are referenced by the same test project.
    public sealed class ApiHostMarker { }
}
