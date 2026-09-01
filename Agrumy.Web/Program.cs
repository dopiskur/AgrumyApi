using System.Globalization;
using api;
using api.Dal.Interface;
using api.Filters;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Refit;

// Form model binding for double/decimal fields (ServerConfig, DeviceConfigController) parses
// with CultureInfo.CurrentCulture, which otherwise follows the host OS locale - on a machine
// where "," is the decimal separator, "8.2" silently parses as 82 (the "." read as a group
// separator) instead of failing validation. Pin it invariant so "8.2" always means 8.2,
// regardless of what locale the box (dev or server) happens to be set to.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Roadmap #104: must run before anything touches the static JwtTokenProvider (LoginController,
// BearerTokenHandler both call JwtTokenProvider.ValidateToken) - this host's real
// builder.Configuration, not Config's old self-built ConfigurationBuilder from
// Directory.GetCurrentDirectory(). Agrumy.Web's own appsettings.json JWT section (must match
// Agrumy.Api's, see README) is what populates Config.secureKey/jwtIssuer/jwtAudience here.
Config.Init(builder.Configuration);

// Base URL of the Agrumy.Api service the views call over HTTP.
var apiServiceUrl = builder.Configuration["WebView:ApiService"];
if (string.IsNullOrEmpty(apiServiceUrl))
    throw new InvalidOperationException("WebView:ApiService is missing in configuration.");

// ApiAuthExceptionFilter turns a 401 from Agrumy.Api (expired stored JWT) into a re-login.
builder.Services.AddControllersWithViews(o => o.Filters.Add<ApiAuthExceptionFilter>());
builder.Services.AddHttpContextAccessor(); // BearerTokenHandler + _Layout read the current user

// Cookie auth: the login POST calls Agrumy.Api, then SignInAsync writes an encrypted ticket
// (cookie name "authorization") carrying the role claim and the JWT as a stored token.
// [Authorize] / [Authorize(Roles = "admin")] on the controllers do the rest.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "authorization";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Deployed behind a TLS-terminating proxy, Kestrel itself only ever sees plain HTTP -
        // the SameAsRequest default would therefore never mark the auth cookie Secure. Always is
        // safe for local dev too: browsers accept Secure cookies on localhost.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.LoginPath = "/Login";
        options.LogoutPath = "/Login/Logout";
        options.AccessDeniedPath = "/Device"; // non-admin hitting an admin page -> back to devices
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });
// Live incident 2026-09-01 (admin.agrumy.com): a stale cookie whose DataProtection key is gone
// crashed every [Authorize] request with an unhandled CryptographicException instead of just
// redirecting to /Login - see SafeTicketDataFormat. PostConfigure so this wraps the REAL default
// TicketDataFormat AddCookie() sets up above, not a null placeholder (registration order matters:
// AddCookie's own PostConfigureOptions runs first because it was added first).
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .PostConfigure<ILoggerFactory>((options, loggerFactory) =>
    {
        options.TicketDataFormat = new SafeTicketDataFormat(
            options.TicketDataFormat, loggerFactory.CreateLogger<SafeTicketDataFormat>());
    });
builder.Services.AddAuthorization();

// Roadmap #79: default DataProtection key storage falls back to the OS user profile, which the
// account the systemd service actually runs as (www-data, per CLAUDE.md - NOT the deploy account
// this code is published under) has no profile directory for on this Linux deployment - keys
// silently regenerate as ephemeral (in-memory only) on every process start. That logs out every
// signed-in user (cookie auth reads the encrypted ticket) and invalidates any open antiforgery-
// protected form on every "build na server" restart.
//
// DataProtection:KeyPath is optional config (appsettings.json/env var), not just a hardcoded
// relative path - the current server happens to run this out of /home/adminagrumy/bin, so
// "sibling of ContentRootPath" resolves to /home/adminagrumy/dataprotection-keys today, but a
// future self-hosted install (roadmap #30) will pick its own user/directory layout and needs to
// be able to point this somewhere its install script actually provisioned (chown'd to the real
// service user) without a code change. The relative-sibling path is only the fallback for when
// nothing is configured - it must still land OUTSIDE ContentRootPath, since "build na server"
// wipes bin/ (ContentRootPath on this deployment) entirely on every publish, which would erase
// keys stored inside it again immediately.
// Directory.CreateDirectory below covers local dev and any host where the parent is already
// writable; if it throws (e.g. www-data lacking permission on the parent directory), keys fall
// back to the previous ephemeral behavior instead of crashing startup - the server still needs a
// one-time `mkdir -p <path> && chmod` (or a real `chown` if run as root) for this to take effect.
try
{
    var configuredKeyPath = builder.Configuration["DataProtection:KeyPath"];
    var keyRingPath = string.IsNullOrWhiteSpace(configuredKeyPath)
        ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "dataprotection-keys"))
        : Path.GetFullPath(configuredKeyPath);
    Directory.CreateDirectory(keyRingPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
        .SetApplicationName("Agrumy.Web");
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"[DataProtection] Could not set up persisted keys, falling back to ephemeral: {ex.Message}");
}

// Refresh-token plumbing: IAuthApi has no BearerTokenHandler (it authenticates by possessing the
// refresh token itself), RefreshCoordinator serializes concurrent refreshes of the same stale token.
builder.Services.AddSingleton<RefreshCoordinator>();
builder.Services
    .AddRefitClient<IAuthApi>(RefitConfig.Settings)
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(apiServiceUrl));

// Declarative Refit client for Agrumy.Api; BearerTokenHandler injects the JWT per request and
// silently refreshes it on a 401 before giving up (see BearerTokenHandler).
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services
    .AddRefitClient<IApi>(RefitConfig.Settings)
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(apiServiceUrl))
    .AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddLogging();

var app = builder.Build();

// Roadmap #69: same wiring as Agrumy.Api's Program.cs - LoginController validates the freshly
// issued JWT through the static JwtTokenProvider, so a rejection here (key mismatch between the
// two appsettings.json files, roadmap #48-class problems) must be visible in THIS process's log.
JwtTokenProvider.Logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(JwtTokenProvider));

// Unhandled exceptions (incl. ApiException from a failed Agrumy.Api call) render /Home/Error.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.MapStaticAssets();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
   .WithStaticAssets();

app.Run();
