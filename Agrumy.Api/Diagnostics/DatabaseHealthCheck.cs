using api.Dal.Interface;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace api.Diagnostics
{
    /// <summary>Reuses <see cref="ISystemRepository.TestConnectionAsync"/>, the same check Program.cs runs at startup, so this reflects whichever provider (MySQL/MariaDB or PostgreSQL) is actually configured, not a provider-specific probe.</summary>
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
