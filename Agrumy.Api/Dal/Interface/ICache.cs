using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Roadmap #72: async so the backing store can be a real distributed cache (Redis,
    /// SQL Server) as well as the in-process default - see CacheRepository.</summary>
    public interface ICache
    {
        /// <summary>Never returns null: a cache miss comes back as a fresh DeviceCache (apiAuth=null),
        /// matching the pre-#72 behaviour every caller already relies on.</summary>
        Task<DeviceCache> GetDeviceCacheAsync(string key);

        /// <summary>Roadmap #109: <paramref name="ttl"/> lets a caller size the sliding expiration
        /// to the device's own sleep interval instead of always taking the 5-minute default (too
        /// short for a device with a multi-hour sleepSeconds, #26/#89) - omit it for the old fixed
        /// behaviour.</summary>
        Task SetItemAsync(string key, DeviceCache deviceCache, TimeSpan? ttl = null);

        /// <summary>Roadmap #118: generic JSON cache for a read-mostly aggregate (e.g. the Fleet
        /// dashboard query) that doesn't fit the device-apiAuth <see cref="DeviceCache"/> shape.
        /// Null means a miss - the caller re-computes and calls <see cref="SetAsync{T}"/>.</summary>
        Task<T?> GetAsync<T>(string key) where T : class;

        /// <summary>Roadmap #118: fixed absolute expiration, not sliding like <see
        /// cref="SetItemAsync"/> - a live dashboard's cached result must go stale on its own after
        /// <paramref name="ttl"/> regardless of how many tabs keep polling it within that window,
        /// otherwise concurrent viewers would keep resetting a sliding timer and the aggregate could
        /// stay stale indefinitely.</summary>
        Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class;
    }
}
