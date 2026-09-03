using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Async so the backing store can be a real distributed cache (Redis, SQL Server) as
    /// well as the in-process default - see CacheRepository.</summary>
    public interface ICache
    {
        /// <summary>Never returns null: a cache miss comes back as a fresh DeviceCache (apiAuth=null).</summary>
        Task<DeviceCache> GetDeviceCacheAsync(string key);

        /// <summary><paramref name="ttl"/> lets a caller size the sliding expiration to the device's
        /// own sleep interval instead of always taking the 5-minute default - omit it for the old
        /// fixed behaviour.</summary>
        Task SetItemAsync(string key, DeviceCache deviceCache, TimeSpan? ttl = null);

        /// <summary>Generic JSON cache for a read-mostly aggregate (e.g. the Fleet dashboard query)
        /// that doesn't fit the device-apiAuth <see cref="DeviceCache"/> shape. Null means a miss -
        /// the caller re-computes and calls <see cref="SetAsync{T}"/>.</summary>
        Task<T?> GetAsync<T>(string key) where T : class;

        /// <summary>Fixed absolute expiration, not sliding like <see cref="SetItemAsync"/> - a live
        /// dashboard's cached result must go stale on its own after <paramref name="ttl"/>
        /// regardless of how many tabs keep polling it within that window.</summary>
        Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class;
    }
}
