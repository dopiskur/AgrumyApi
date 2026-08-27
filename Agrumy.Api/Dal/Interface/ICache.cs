using api.Models;

namespace api.Dal.Interface
{
    public interface ICache
    {

        public bool KeyValid(string key);
        public DeviceCache? GetDeviceCache(string key);
        public void SetItem(string key, DeviceCache deviceCache);



    }
}
