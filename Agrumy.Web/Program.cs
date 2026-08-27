using api.Dal;
using api.Dal.Interface;

var builder = WebApplication.CreateBuilder(args);

// Base URL of the Agrumy.Api service the views call over HTTP.
var apiServiceUrl = builder.Configuration["WebView:ApiService"];
if (string.IsNullOrEmpty(apiServiceUrl))
    throw new InvalidOperationException("WebView:ApiService is missing in configuration.");

// MVC + views
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor(); // used by _Layout / views for the auth cookie

// HttpClient for the HTTP-backed API repository
builder.Services.AddHttpClient<IApi, ApiRepository>(c => c.BaseAddress = new Uri(apiServiceUrl));

builder.Services.AddLogging();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
