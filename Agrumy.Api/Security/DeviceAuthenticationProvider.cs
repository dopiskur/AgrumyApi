using api.Dal.Interface;
using api.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

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
            if (device == null || device.ApiKey is null)
            {
                return false;
            }

            // Constant-time comparison of the provided apiKey against the stored one.
            byte[] providedKey = Encoding.UTF8.GetBytes(apikey.ToString());
            byte[] storedKey = Encoding.UTF8.GetBytes(device.ApiKey);

            // FixedTimeEquals requires equal-length inputs; a length mismatch is a non-match
            // and must not throw or leak information.
            if (providedKey.Length != storedKey.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(providedKey, storedKey);
        }
    }
}
