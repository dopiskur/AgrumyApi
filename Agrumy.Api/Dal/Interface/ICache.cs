using api.Models;

namespace api.Dal.Interface
{
    /// Async so the backing store can be a real distributed cache (Redis, SQL Server) as well as the in-process default - see CacheRepository.
    public interface ICache
    {
        /// A cache miss comes back as a fresh DeviceCache (apiAuth=null), never null.
        Task<DeviceCache> GetDeviceCacheAsync(string key);

        /// <paramref name="ttl"/> sizes the sliding expiration to the device's own sleep interval instead of the fixed 5-minute default.
        Task SetItemAsync(string key, DeviceCache deviceCache, TimeSpan? ttl = null);

        /// Generic JSON cache for a read-mostly aggregate that doesn't fit DeviceCache's shape - null means a miss, the caller re-computes and calls SetAsync.
        Task<T?> GetAsync<T>(string key) where T : class;

        /// Fixed absolute expiration, not sliding like SetItemAsync - a cached dashboard result must go stale after <paramref name="ttl"/> regardless of poll frequency.
        Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class;

        /// Drops a GetAsync/SetAsync entry early - for a write that must be visible before its TTL would otherwise expire.
        Task RemoveAsync(string key);
    }
}
