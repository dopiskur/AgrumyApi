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
        Task SetItemAsync(string key, DeviceCache deviceCache);
    }
}
