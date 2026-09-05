using System.Text.Json;

namespace Agrumy.Api.Tests;

/// Regression guard for the JWT SecureKey/Issuer/Audience mismatch that once made Agrumy.Web logins silently fail (JwtTokenProvider.ValidateToken returns null on mismatch) - runs the comparison only when both gitignored appsettings.json files are present on disk, and no-ops on a checkout without deploy secrets (e.g. CI).
public class JwtConfigParityTests
{
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "agrumy.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void ApiAndWeb_JwtSettings_Match()
    {
        string? root = FindRepoRoot();
        if (root is null) return;

        string apiPath = Path.Combine(root, "Agrumy.Api", "appsettings.json");
        string webPath = Path.Combine(root, "Agrumy.Web", "appsettings.json");
        if (!File.Exists(apiPath) || !File.Exists(webPath)) return;

        using JsonDocument apiDoc = JsonDocument.Parse(File.ReadAllText(apiPath));
        using JsonDocument webDoc = JsonDocument.Parse(File.ReadAllText(webPath));
        JsonElement apiJwt = apiDoc.RootElement.GetProperty("JWT");
        JsonElement webJwt = webDoc.RootElement.GetProperty("JWT");

        Assert.Equal(apiJwt.GetProperty("SecureKey").GetString(), webJwt.GetProperty("SecureKey").GetString());
        Assert.Equal(apiJwt.GetProperty("Issuer").GetString(), webJwt.GetProperty("Issuer").GetString());
        Assert.Equal(apiJwt.GetProperty("Audience").GetString(), webJwt.GetProperty("Audience").GetString());
    }
}
