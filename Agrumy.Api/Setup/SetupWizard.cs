using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using api.Dal;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace api.Setup
{
    /// If ConnectionStrings:DefaultConnection is missing at boot, Program.cs routes here instead of the normal pipeline - an unauthenticated DB-details-only form that writes appsettings.json and restarts (RestartUtil) once the admin submits a connection that opens; container installs never reach this since install.sh always sets it upfront.
    internal static class SetupWizard
    {
        // One-time, process-lifetime token gating the wizard (roadmap #321/#248) - closes the window
        // between the service starting and the admin reaching the page: whoever gets there first over
        // the network still needs this value, which only reaches the service's own log (same pattern
        // as the bootstrap Global Admin setup secret).
        private static readonly string SetupToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        public static void ConfigureServices(WebApplicationBuilder builder)
        {
            // The wizard's only real risk window is "whoever reaches this unauthenticated page
            // before the admin does" - antiforgery is a cheap extra guard against a cross-site POST specifically.
            builder.Services.AddAntiforgery();
            builder.Services.AddLogging();
        }

        public static void LogSetupToken(ILogger logger) =>
            logger.LogWarning("Setup wizard token (required in the URL, works once): open this page as ?token={SetupToken}", SetupToken);

        public static void MapEndpoints(WebApplication app)
        {
            app.MapGet("/", (IAntiforgery antiforgery, HttpContext context) =>
            {
                if (!TokenMatches(context))
                {
                    return Results.Content(RenderTokenRequired(), "text/html", statusCode: 403);
                }
                AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
                return Results.Content(RenderForm(tokens.RequestToken, error: null), "text/html");
            });

            app.MapPost("/", async (
                HttpContext context,
                IAntiforgery antiforgery,
                IWebHostEnvironment env,
                IHostApplicationLifetime lifetime,
                ILogger<Program> logger) =>
            {
                if (!TokenMatches(context))
                {
                    return Results.Content(RenderTokenRequired(), "text/html", statusCode: 403);
                }

                // Every rejection path re-renders the form with its OWN fresh token - reusing the just-submitted (single-use) one would fail the resend the same way an expired token does.
                string FreshToken() => antiforgery.GetAndStoreTokens(context).RequestToken!;

                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.Content(RenderForm(FreshToken(), "Form expired - reload the page and try again."), "text/html", statusCode: 400);
                }

                IFormCollection form = await context.Request.ReadFormAsync();
                string provider = form["provider"].ToString();
                string host = form["host"].ToString().Trim();
                string port = form["port"].ToString().Trim();
                string database = form["database"].ToString().Trim();
                string username = form["username"].ToString().Trim();
                string password = form["password"].ToString();

                if (host.Length == 0 || port.Length == 0 || database.Length == 0 || username.Length == 0)
                {
                    return Results.Content(RenderForm(FreshToken(), "Host, port, database and username are all required."), "text/html", statusCode: 400);
                }

                DbProviderKind providerKind;
                try
                {
                    providerKind = DbProviderKindParser.Parse(provider);
                }
                catch (InvalidOperationException)
                {
                    return Results.Content(RenderForm(FreshToken(), "Unrecognized database type - pick one of the two options."), "text/html", statusCode: 400);
                }
                string connectionString = providerKind == DbProviderKind.Postgres
                    ? $"Host={host};Port={port};Database={database};Username={username};Password={password}"
                    : $"server={host};port={port};database={database};user id={username};password={password};SslMode=Preferred;";

                bool canConnect;
                try
                {
                    await using var testContext = new AgrumyDbContext(DbOptionsFactory.Build(providerKind, connectionString));
                    canConnect = await testContext.Database.CanConnectAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Setup wizard: connection test failed.");
                    canConnect = false;
                }

                if (!canConnect)
                {
                    return Results.Content(RenderForm(FreshToken(), "Could not connect with those details - check host/port/database/username/password and that the database server accepts connections from this machine."), "text/html", statusCode: 400);
                }

                await WriteConnectionStringAsync(env.ContentRootPath, providerKind, connectionString);
                RestartUtil.ScheduleRestart(lifetime, logger, "setup wizard: connection string saved");

                return Results.Content(RenderRestarting(), "text/html");
            });
        }

        /// Atomic write (temp file + rename) so a crash mid-write never leaves a half-written appsettings.json; merges into whatever already exists (JWT keys etc.) rather than replacing it, tolerating a missing file.
        private static async Task WriteConnectionStringAsync(string contentRootPath, DbProviderKind provider, string connectionString)
        {
            string path = Path.Combine(contentRootPath, "appsettings.json");
            JsonNode root = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path)) ?? new JsonObject()
                : new JsonObject();

            root["ConnectionStrings"] ??= new JsonObject();
            root["ConnectionStrings"]!["DefaultConnection"] = connectionString;
            root["Database"] ??= new JsonObject();
            root["Database"]!["Provider"] = provider == DbProviderKind.Postgres ? "postgres" : "mysql";

            string tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, overwrite: true);
        }

        private static bool TokenMatches(HttpContext context) =>
            context.Request.Query.TryGetValue("token", out var supplied) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied.ToString()), Encoding.UTF8.GetBytes(SetupToken));

        private static string RenderTokenRequired() => """
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8" />
                <title>Agrumy - Setup token required</title>
                <style>body { font-family: sans-serif; max-width: 32rem; margin: 3rem auto; padding: 0 1rem; }</style>
            </head>
            <body>
                <h1>Setup token required</h1>
                <p>Missing or incorrect <code>?token=</code> in the URL. Check this service's own log (e.g. <code>journalctl -u agrumy-api</code>) for a line starting "Setup wizard token".</p>
            </body>
            </html>
            """;

        private static string RenderForm(string? antiforgeryToken, string? error)
        {
            string errorHtml = error is null ? "" : $"""<p style="color:#b00;font-weight:bold">{System.Net.WebUtility.HtmlEncode(error)}</p>""";
            string tokenField = antiforgeryToken is null ? "" : $"""<input type="hidden" name="__RequestVerificationToken" value="{System.Net.WebUtility.HtmlEncode(antiforgeryToken)}" />""";

            return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8" />
                <title>Agrumy - Database setup</title>
                <style>
                    body { font-family: sans-serif; max-width: 32rem; margin: 3rem auto; padding: 0 1rem; }
                    label { display: block; margin-top: 0.75rem; font-weight: bold; }
                    input { width: 100%; padding: 0.4rem; box-sizing: border-box; }
                    button { margin-top: 1.5rem; padding: 0.6rem 1.2rem; }
                    .radio-row { display: flex; gap: 1.5rem; margin-top: 0.5rem; }
                    .radio-row label { font-weight: normal; margin-top: 0; }
                </style>
            </head>
            <body>
                <h1>Agrumy - Database setup</h1>
                <p>This install has no database connection configured yet. Fill in your database's details below - Agrumy will provision its schema automatically on the first connection.</p>
                {{errorHtml}}
                <form method="post" action="?token={{Uri.EscapeDataString(SetupToken)}}">
                    {{tokenField}}
                    <label>Database type</label>
                    <div class="radio-row">
                        <label><input type="radio" name="provider" value="mysql" checked onchange="document.getElementById('port').value='3306'" /> MySQL / MariaDB</label>
                        <label><input type="radio" name="provider" value="postgres" onchange="document.getElementById('port').value='5432'" /> PostgreSQL</label>
                    </div>
                    <label for="host">Host</label>
                    <input id="host" name="host" value="localhost" required />
                    <label for="port">Port</label>
                    <input id="port" name="port" value="3306" required />
                    <label for="database">Database name</label>
                    <input id="database" name="database" value="agrumy" required />
                    <label for="username">Username</label>
                    <input id="username" name="username" required />
                    <label for="password">Password</label>
                    <input id="password" name="password" type="password" />
                    <button type="submit">Save and continue</button>
                </form>
            </body>
            </html>
            """;
        }

        private static string RenderRestarting() => """
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="utf-8" />
                <title>Agrumy - Restarting</title>
                <meta http-equiv="refresh" content="8" />
                <style>body { font-family: sans-serif; max-width: 32rem; margin: 3rem auto; padding: 0 1rem; }</style>
            </head>
            <body>
                <h1>Saved - restarting&hellip;</h1>
                <p>Connection details saved. Agrumy is restarting with the new database - this page will reload automatically in a few seconds.</p>
            </body>
            </html>
            """;
    }
}
