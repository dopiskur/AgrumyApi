using System.Globalization;
using api;
using api.Dal.Interface;
using api.Filters;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Refit;

// Pinned invariant so a host OS locale with "," as decimal separator doesn't silently
// misparse form fields like "8.2" as 82 under CultureInfo.CurrentCulture.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Must run before anything touches the static JwtTokenProvider (LoginController,
// BearerTokenHandler); Agrumy.Web's JWT section here must match Agrumy.Api's, see README.
Config.Init(builder.Configuration);

var apiServiceUrl = builder.Configuration["WebView:ApiService"];
if (string.IsNullOrEmpty(apiServiceUrl))
    throw new InvalidOperationException("WebView:ApiService is missing in configuration.");

// ApiAuthExceptionFilter turns a 401 from Agrumy.Api (expired stored JWT) into a re-login.
builder.Services.AddControllersWithViews(o => o.Filters.Add<ApiAuthExceptionFilter>());
builder.Services.AddHttpContextAccessor(); // BearerTokenHandler + _Layout read the current user

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "authorization";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Deployed behind a TLS-terminating proxy, Kestrel itself only ever sees plain HTTP -
        // SameAsRequest would never mark the cookie Secure. Always is safe for local dev too.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/Login";
        options.LogoutPath = "/Login/Logout";
        options.AccessDeniedPath = "/Device"; // non-admin hitting an admin page -> back to devices
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });
// Wraps the real default TicketDataFormat AddCookie() sets up above (see SafeTicketDataFormat)
// so a stale cookie whose DataProtection key is gone redirects to /Login instead of throwing.
// Registration order matters: AddCookie's own PostConfigureOptions runs first because it was added first.
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .PostConfigure<ILoggerFactory>((options, loggerFactory) =>
    {
        options.TicketDataFormat = new SafeTicketDataFormat(
            options.TicketDataFormat, loggerFactory.CreateLogger<SafeTicketDataFormat>());
    });
builder.Services.AddAuthorization();

try
{
    var configuredKeyPath = builder.Configuration["DataProtection:KeyPath"];
    var keyRingPath = string.IsNullOrWhiteSpace(configuredKeyPath)
        ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "dataprotection-keys"))
        : Path.GetFullPath(configuredKeyPath);
    // Must resolve outside ContentRootPath - "build na server" wipes bin/ (ContentRootPath
    // on this deployment) on every publish, which would erase keys stored inside it.
    Directory.CreateDirectory(keyRingPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .SetApplicationName("Agrumy.Web");
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"[DataProtection] Could not set up persisted keys, falling back to ephemeral: {ex.Message}");
}

// IAuthApi has no BearerTokenHandler (it authenticates by possessing the refresh token
// itself); RefreshCoordinator serializes concurrent refreshes of the same stale token.
builder.Services.AddSingleton<RefreshCoordinator>();
builder.Services
    .AddRefitClient<IAuthApi>(RefitConfig.Settings)
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(apiServiceUrl));

// BearerTokenHandler injects the JWT per request and silently refreshes it on a 401 before giving up.
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services
    .AddRefitClient<IApi>(RefitConfig.Settings)
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(apiServiceUrl))
    .AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddLogging();

var app = builder.Build();

// LoginController validates the freshly issued JWT through the static JwtTokenProvider, so
// a rejection here (key mismatch between the two appsettings.json files) must be logged in THIS process.
JwtTokenProvider.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JwtTokenProvider));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// esp-web-tools ships extensionless ES-module imports (e.g. "./connect") that jsDelivr
// resolves but a plain static-file server 404s on - append ".js" only for this vendored path.
app.Use(async (context, next) =>
{
    PathString requestPath = context.Request.Path;
    if (requestPath.StartsWithSegments("/lib/esp-web-tools", out _) &&
        string.IsNullOrEmpty(Path.GetExtension(requestPath.Value)) &&
        File.Exists(Path.Combine(app.Environment.WebRootPath, requestPath.Value!.TrimStart('/') + ".js")))
    {
        context.Request.Path = requestPath + ".js";
    }
    await next();
});

app.UseMiddleware<SecurityHeadersMiddleware>();

app.MapStaticAssets();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
   .WithStaticAssets();

app.Run();
