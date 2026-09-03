using System.Runtime.CompilerServices;
using api;
using Microsoft.Extensions.Configuration;

namespace Agrumy.Api.Tests;

/// <summary>Config (Agrumy.Shared) no longer self-initializes; each host's Program.cs calls Config.Init() once at startup. This test assembly is not a host, so [ModuleInitializer] guarantees the same call happens once regardless of test ordering/parallelization, reading the same appsettings.json AgrumySettings.Bind(Configuration) uses elsewhere - so a signed-then-validated JWT round-trip uses the same key on both ends.</summary>
internal static class TestConfig
{
    public static readonly IConfigurationRoot Configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json")
        .Build();

    [ModuleInitializer]
    internal static void Init() => Config.Init(Configuration);
}
