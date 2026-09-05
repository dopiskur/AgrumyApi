using System.Text.Json;
using api.Dal.Interface;
using api.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace api.Dal
{
    /// IDistributedCache-backed device apiAuth session cache - still in-process today (AddDistributedMemoryCache in Program.cs), but swapping to Redis later is a one-line Program.cs change.
    internal sealed partial class CacheRepository(IDistributedCache cache, ILogger<CacheRepository> logger) : ICache
    {
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
                // Caught as plain Exception since IDistributedCache is backend-agnostic - a miss here
                // just costs one extra device auth round-trip, not correctness.
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
                // Caller treats null as "recompute" - a backend outage becomes a cache-miss instead of a 500.
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
