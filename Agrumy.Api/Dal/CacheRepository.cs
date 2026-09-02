using System.Text.Json;
using api.Dal.Interface;
using api.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace api.Dal
{
    /// <summary>
    /// Roadmap #72: IDistributedCache instead of the old process-local System.Runtime.Caching.MemoryCache -
    /// today it's backed by AddDistributedMemoryCache() (Program.cs), so behaviour on a single instance is
    /// unchanged (still in-process, still lost on restart), but the device apiAuth session this stores is
    /// what a scale-out deployment needs shared across instances - swapping to a real backend (Redis, SQL
    /// Server) at that point is a one-line Program.cs change, not a rewrite of this class or its callers.
    /// </summary>
    internal class CacheRepository(IDistributedCache cache) : ICache
    {
        // Roadmap #109: fallback for a caller that doesn't size its own TTL (SetItemAsync's ttl
        // param) - kept as the default so a short-poll device's behaviour is unchanged.
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

        public async Task<DeviceCache> GetDeviceCacheAsync(string key)
        {
            byte[]? bytes = await cache.GetAsync(key);
            return bytes is null
                ? new DeviceCache { apiAuth = null }
                : JsonSerializer.Deserialize<DeviceCache>(bytes)!;
        }

        public Task SetItemAsync(string key, DeviceCache deviceCache, TimeSpan? ttl = null) =>
            cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(deviceCache),
                new DistributedCacheEntryOptions { SlidingExpiration = ttl ?? DefaultTtl });

        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            byte[]? bytes = await cache.GetAsync(key);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class =>
            cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    }
}
