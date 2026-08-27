using api.Dal.Interface;
using api.Models;
using System.Net.Http.Headers;

namespace api.Security
{
    /// <summary>
    /// Device apiId/apiKey verification. Split out of <see cref="AuthenticationProvider"/> because it
    /// needs the data-access layer (RepoFactory), which the shared assembly must not reference.
    /// </summary>
    public class DeviceAuthenticationProvider
    {
        public static async Task<bool> VerifyDeviceAsync(AuthenticationHeaderValue apiId, AuthenticationHeaderValue apikey)
        {

            Device device = await RepoFactory.GetRepo().DeviceGetAsync(0, null, apiId.ToString(), null); //popravi tenant
            if (device == null)
            {
                // upisi u log da device ne postoji
                return false;
            }

            if (apikey.ToString() == device.ApiKey)
            {
                return true;
            }

            // Cleaning MemoryCache manually if item exists
            // RepoFactory.GetCache().RemoveItem(apiId.ToString());

            return false;
        }
    }
}
