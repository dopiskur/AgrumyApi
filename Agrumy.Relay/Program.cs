using api.Models;
using api.Relay;
using api.Relay.ChirpStack;
using api.Relay.LocalForwarding;
using api.Relay.Registration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RelayOptions>(builder.Configuration);

builder.Services.AddSingleton<RelayRegistrationStore>();
builder.Services.AddHostedService<RelayRegistrationService>();

builder.Services.AddHttpClient<AgrumyServiceClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RelayOptions>>().Value;
    http.BaseAddress = new Uri(opts.AgrumyService.BaseUrl);
});

RelayOptions relayOptions = builder.Configuration.Get<RelayOptions>() ?? new RelayOptions();
if (relayOptions.Relay.Profile == RelayProfile.LoRaGateway)
{
    builder.Services.AddHostedService<ChirpStackUplinkService>();
}

// Profile A's own local listener port - separate from whatever port AgrumyService itself uses,
// since a relay and its upstream are never the same process.
builder.WebHost.ConfigureKestrel(k => k.ListenAnyIP(relayOptions.Relay.LocalPort));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapProfileAEndpoints();

app.Run();
