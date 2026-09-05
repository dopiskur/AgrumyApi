using api.Models;
using api.Gateway;
using api.Gateway.ChirpStack;
using api.Gateway.LocalForwarding;
using api.Gateway.Registration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration);

builder.Services.AddSingleton<GatewayRegistrationStore>();
builder.Services.AddHostedService<GatewayRegistrationService>();

builder.Services.AddHttpClient<AgrumyServiceClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GatewayOptions>>().Value;
    http.BaseAddress = new Uri(opts.AgrumyService.BaseUrl);
});

GatewayOptions gatewayOptions = builder.Configuration.Get<GatewayOptions>() ?? new GatewayOptions();
if (gatewayOptions.Gateway.Profile == GatewayProfile.LoRaGateway)
{
    builder.Services.AddHostedService<ChirpStackUplinkService>();
}

builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(gatewayOptions.Gateway.LocalPort)); // Profile A's own port, separate from whatever port AgrumyService itself uses

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapProfileAEndpoints();

app.Run();
