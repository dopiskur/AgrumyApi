using api.Models;

namespace api.Dal.Interface
{
    public interface ICache
    {
        DeviceCache? GetDeviceCache(string key);
        void SetItem(string key, DeviceCache deviceCache);
    }
}
