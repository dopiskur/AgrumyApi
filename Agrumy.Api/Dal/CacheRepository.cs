using api.Dal.Interface;
using api.Models;
using System.Runtime.Caching;

namespace api.Dal
{
    internal class CacheRepository : ICache
    {
        private static readonly MemoryCache CacheDevice = new("ApiKey");

        private static readonly CacheItemPolicy GlobalCacheItemPolicy = new()
        {
            SlidingExpiration = TimeSpan.FromMinutes(5), // drop the item after 5 min of inactivity
        };

        public DeviceCache? GetDeviceCache(string key)
        {
            return CacheDevice.Get(key) as DeviceCache
                   ?? new DeviceCache { ConfigVersion = 0, apiAuth = null };
        }

        public void SetItem(string key, DeviceCache deviceCache)
        {
            // Set does insert-or-update in one step.
            CacheDevice.Set(new CacheItem(key, deviceCache), GlobalCacheItemPolicy);
        }
    }
}
