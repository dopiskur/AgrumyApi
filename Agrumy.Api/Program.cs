using api.Dal;
using api.Dal.Interface;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure JWT security services
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

builder.Services.AddControllers();

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

    // Device onboarding / auth / config poll. A device wakes ~every SleepSeconds (default 60s)
    // and does ~1 Authenticate + ~1 Config per wake => ~2 req/min per device. 20/min/IP leaves
    // headroom for ~10 devices behind one NAT/public IP plus retries.
    options.AddPolicy("device-auth", httpContext => IpFixedWindow(httpContext, 20));

    // Device telemetry push (SensorData POST). ~1 push/min per device; higher ceiling for many
    // devices sharing one NAT and for catch-up bursts after a device was offline.
    options.AddPolicy("device-data", httpContext => IpFixedWindow(httpContext, 60));
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// Logging
builder.Services.AddLogging();

// dependency injection
builder.Services.AddScoped<IRepository, SqlRepository>();
builder.Services.AddScoped<ICache, CacheRepository>();

// CONFIGURE SWAGGER FOR JWT
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

// BUILD CONFIG
var app = builder.Build();

// ALWAYS USE SWAGGER
app.UseSwagger();
app.UseSwaggerUI();
// END SWAGGER

// Middleware order is important
app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Startup DB health-check + optional schema auto-provisioning
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
