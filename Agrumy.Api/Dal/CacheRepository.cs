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
        private static readonly DistributedCacheEntryOptions Options = new()
        {
            SlidingExpiration = TimeSpan.FromMinutes(5), // drop the item after 5 min of inactivity
        };

        public async Task<DeviceCache> GetDeviceCacheAsync(string key)
        {
            byte[]? bytes = await cache.GetAsync(key);
            return bytes is null
                ? new DeviceCache { apiAuth = null }
                : JsonSerializer.Deserialize<DeviceCache>(bytes)!;
        }

        public Task SetItemAsync(string key, DeviceCache deviceCache) =>
            cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(deviceCache), Options);
    }
}
