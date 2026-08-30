using api.Dal.Interface;
using api.Filters;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authentication.Cookies;
using Refit;

var builder = WebApplication.CreateBuilder(args);

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
        // SecurePolicy defaults to SameAsRequest: Secure over HTTPS, not over plain-HTTP dev.
        options.LoginPath = "/Login";
        options.LogoutPath = "/Login/Logout";
        options.AccessDeniedPath = "/Device"; // non-admin hitting an admin page -> back to devices
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

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
