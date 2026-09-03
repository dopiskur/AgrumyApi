using api.Dal.Interface;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace api.Diagnostics
{
    /// <summary>Roadmap #143. Reuses <see cref="ISystemRepository.TestConnectionAsync"/> - the same
    /// check Program.cs already runs at startup - so this reflects whichever provider (MySQL/MariaDB
    /// or PostgreSQL, roadmap #14) the running instance is actually configured against, not a
    /// provider-specific probe.</summary>
    internal sealed class DatabaseHealthCheck(ISystemRepository repository) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                return await repository.TestConnectionAsync()
                    ? HealthCheckResult.Healthy("Database connection OK.")
                    : HealthCheckResult.Unhealthy("Database connection failed.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Database connection threw an exception.", ex);
            }
        }
    }
}
