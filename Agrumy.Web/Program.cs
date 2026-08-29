using api.Dal.Interface;
using api.Utils;
using Refit;

var builder = WebApplication.CreateBuilder(args);

// Base URL of the Agrumy.Api service the views call over HTTP.
var apiServiceUrl = builder.Configuration["WebView:ApiService"];
if (string.IsNullOrEmpty(apiServiceUrl))
    throw new InvalidOperationException("WebView:ApiService is missing in configuration.");

// MVC + views
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor(); // used by _Layout / views for the auth cookie

// Declarative Refit client for Agrumy.Api (IHttpClientFactory-managed HttpClient underneath).
builder.Services
    .AddRefitClient<IApi>(RefitConfig.Settings)
    .ConfigureHttpClient(c => c.BaseAddress = new Uri(apiServiceUrl));

builder.Services.AddLogging();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
