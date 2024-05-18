using api.Dal.Interface;
using api.Models;
using System.Net.Http.Headers;
using System.Runtime.Caching;

namespace api.Dal
{
    internal class CacheRepository : ICache
    {

        private static MemoryCache CacheDevice = new MemoryCache("ApiKey");


        public CacheRepository()
        {
        }

        private static CacheItemPolicy GlobalCacheItemPolicy = new()
        {
            AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(120)
            //SlidingExpiration = TimeSpan.FromMinutes(120) // u sekundama, brise item nakon 5min neaktivnosti
        };


        public bool KeyValid(string key)
        {
            return CacheDevice.Contains(key);
        }

        public DeviceCache? GetDeviceCache(string key)
        {
            // ne mozes direktno toStringat value, ako je potencijalno null
            DeviceCache? deviceCache = new DeviceCache();
            DeviceCache value = (DeviceCache)CacheDevice.Get(key); // ako je null, stavi prazni string
            if(value == null)
            {
                deviceCache.ConfigVersion = 0;
                deviceCache.apiAuth = null;
            }
            else
            {
                deviceCache = value;
            }
            return deviceCache; 
        }

        public void SetItem(string key, DeviceCache deviceCache)
        {
            // koristimo set jer set radi insert ili update, bolje nego remove i add, ne ostavlja rupe u listi
            CacheDevice.Set(new CacheItem(key, deviceCache), GlobalCacheItemPolicy);
            
        }
    }
}
