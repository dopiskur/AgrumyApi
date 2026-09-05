using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace api.Diagnostics
{
    /// Probes the raw <see cref="IDistributedCache"/> directly, not through <see cref="api.Dal.Interface.ICache"/> which swallows backend exceptions and would hide the failure; a failed round-trip reports Degraded, not Unhealthy.
    internal sealed class CacheHealthCheck(IDistributedCache cache) : IHealthCheck
    {
        private const string ProbeKey = "healthcheck:cache-probe";
        private static readonly byte[] ProbeValue = [1];

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await cache.SetAsync(ProbeKey, ProbeValue,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) },
                    cancellationToken);
                await cache.GetAsync(ProbeKey, cancellationToken);
                return HealthCheckResult.Healthy("Cache backend OK.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Degraded("Cache backend unreachable - app continues without it.", ex);
            }
        }
    }
}
