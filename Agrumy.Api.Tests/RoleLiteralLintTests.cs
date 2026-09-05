using System.Text.RegularExpressions;

namespace Agrumy.Api.Tests;

/// Roadmap #299: three parallel authorization styles (raw string literal, a hand-built "admin,"+RoleNames.X hybrid, and RoleNames.* constants) were a source of security holes - a future [Authorize(Roles = "...")] with a raw string literal must fail CI instead of silently reintroducing the inconsistency.
public class RoleLiteralLintTests
{
    private static readonly Regex RawRoleLiteral = new("Authorize\\s*\\(\\s*Roles\\s*=\\s*\"", RegexOptions.Compiled);

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
    public void NoControllerUsesARawStringRoleLiteral()
    {
        string? root = FindRepoRoot();
        if (root is null) return;

        var offenders = new List<string>();
        foreach (string project in new[] { "Agrumy.Api", "Agrumy.Web" })
        {
            string dir = Path.Combine(root, project, "Controllers");
            if (!Directory.Exists(dir)) continue;

            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                foreach (string line in File.ReadLines(file))
                {
                    if (RawRoleLiteral.IsMatch(line))
                    {
                        offenders.Add($"{Path.GetRelativePath(root, file)}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Use a RoleNames.* constant instead of a raw string literal in [Authorize(Roles = ...)]:\n" + string.Join("\n", offenders));
    }
}
