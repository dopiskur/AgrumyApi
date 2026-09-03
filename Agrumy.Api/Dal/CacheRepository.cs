using System.Text.Json;
using api.Dal.Interface;
using api.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace api.Dal
{
    /// <summary>
    /// Roadmap #72: IDistributedCache instead of the old process-local System.Runtime.Caching.MemoryCache -
    /// today it's backed by AddDistributedMemoryCache() (Program.cs), so behaviour on a single instance is
    /// unchanged (still in-process, still lost on restart), but the device apiAuth session this stores is
    /// what a scale-out deployment needs shared across instances - swapping to a real backend (Redis, SQL
    /// Server) at that point is a one-line Program.cs change, not a rewrite of this class or its callers.
    /// </summary>
    internal sealed partial class CacheRepository(IDistributedCache cache, ILogger<CacheRepository> logger) : ICache
    {
        // Roadmap #109: fallback for a caller that doesn't size its own TTL (SetItemAsync's ttl
        // param) - kept as the default so a short-poll device's behaviour is unchanged.
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Cache backend unavailable during {Operation} (key {Key}) - degrading to miss/no-op.")]
        private static partial void LogBackendUnavailable(ILogger logger, Exception ex, string operation, string key);

        public async Task<DeviceCache> GetDeviceCacheAsync(string key)
        {
            byte[]? bytes;
            try
            {
                bytes = await cache.GetAsync(key);
            }
            catch (Exception ex)
            {
                // Roadmap #119: IDistributedCache is backend-agnostic, so a down/unreachable Redis
                // (once #30/#72 wire it in) throws whatever its own client library uses (connection
                // refused, timeout, ...) - caught here as plain Exception rather than a Redis-specific
                // type so this stays correct regardless of which backend is configured. A device's
                // apiAuth session isn't durable state (it's re-derived by re-authenticating against the
                // DB), so a miss here costs one extra auth round-trip, not correctness.
                LogBackendUnavailable(logger, ex, nameof(GetDeviceCacheAsync), key);
                return new DeviceCache { apiAuth = null };
            }
            return bytes is null
                ? new DeviceCache { apiAuth = null }
                : JsonSerializer.Deserialize<DeviceCache>(bytes)!;
        }

        public async Task SetItemAsync(string key, DeviceCache deviceCache, TimeSpan? ttl = null)
        {
            try
            {
                await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(deviceCache),
                    new DistributedCacheEntryOptions { SlidingExpiration = ttl ?? DefaultTtl });
            }
            catch (Exception ex)
            {
                // A dropped write just means the next auth check misses and the device re-authenticates -
                // no different from the entry never having been written at all.
                LogBackendUnavailable(logger, ex, nameof(SetItemAsync), key);
            }
        }

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            byte[]? bytes;
            try
            {
                bytes = await cache.GetAsync(key);
            }
            catch (Exception ex)
            {
                // Roadmap #118 caller (e.g. the Fleet dashboard query) treats null as "recompute" -
                // a backend outage becomes a cache-miss recompute instead of a 500.
                LogBackendUnavailable(logger, ex, nameof(GetAsync), key);
                return null;
            }
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class
        {
            try
            {
                await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
            }
            catch (Exception ex)
            {
                LogBackendUnavailable(logger, ex, nameof(SetAsync), key);
            }
        }
    }
}
