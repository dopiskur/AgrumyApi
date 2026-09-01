using System.Runtime.CompilerServices;
using api;
using Microsoft.Extensions.Configuration;

namespace Agrumy.Api.Tests;

/// <summary>
/// Roadmap #104: Config (Agrumy.Shared) no longer self-initializes from the working directory -
/// each host's Program.cs calls Config.Init() once at startup. This test assembly is not a host,
/// so it needs the same call made once before any test runs; [ModuleInitializer] guarantees that
/// regardless of test ordering/parallelization. Reads the same appsettings.json the test project
/// already ships (see its own "// NOTE") - AgrumySettings.Bind(Configuration) (used wherever a
/// test needs an IOptions&lt;AgrumySettings&gt;, e.g. ApiControllerTests.NewUserController) reads
/// the identical source, so a signed-then-validated JWT round-trip uses the same key on both ends.
/// </summary>
internal static class TestConfig
{
    public static readonly IConfigurationRoot Configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json")
        .Build();

    [ModuleInitializer]
    internal static void Init() => Config.Init(Configuration);
}
