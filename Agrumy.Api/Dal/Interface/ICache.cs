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
    }
}
