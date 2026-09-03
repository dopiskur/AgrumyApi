using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace api.Diagnostics
{
    /// <summary>Roadmap #143, ties into #119's graceful-degradation decision: probes the raw
    /// <see cref="IDistributedCache"/> directly rather than going through <see
    /// cref="api.Dal.Interface.ICache"/>, because CacheRepository (roadmap #119) deliberately
    /// swallows every backend exception and degrades to a miss/no-op - the same behaviour that keeps
    /// the app running with a dead cache would also hide the failure from this check if probed
    /// through it. A failed round-trip here reports Degraded, not Unhealthy: the API stays fully
    /// functional (worst case is extra DB re-auth/recompute traffic), so this must never flip
    /// #139's post-restart health check to a failing 503.</summary>
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
                return HealthCheckResult.Degraded("Cache backend unreachable - app continues without it (roadmap #119).", ex);
            }
        }
    }
}
