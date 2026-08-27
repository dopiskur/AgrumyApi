using api.Dal;
using api.Dal.Interface;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure JWT security services
var secureKey = builder.Configuration["JWT:SecureKey"];
if (string.IsNullOrEmpty(secureKey))
    throw new InvalidOperationException("JWT:SecureKey is missing in configuration.");

bool webViewEnabled = bool.TryParse(builder.Configuration["WebView:Enabled"], out var enabled) && enabled;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => {
        var Key = Encoding.UTF8.GetBytes(secureKey);
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKey = new SymmetricSecurityKey(Key)
        };
    });

builder.Services.AddControllers();

if (webViewEnabled)
{
    builder.Services.AddControllersWithViews(); // OPTIONAL -> viewEnabled
    builder.Services.AddHttpContextAccessor(); // COOKIE MONSTER
                                               // HTTPClient
    builder.Services.AddHttpClient<IApi, ApiRepository>(
        c => c.BaseAddress = new Uri("http://localhost:5000")); // dependency injection za IAPI
}

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

if (webViewEnabled)
{
    app.UseStaticFiles(); // ViewEnabled?
    app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"); // ViewEnabled
}

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
