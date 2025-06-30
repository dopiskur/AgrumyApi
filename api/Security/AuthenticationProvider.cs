using api.Dal.Interface;
using api.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace api.Security
{
    public class AuthenticationProvider
    {
        const int keySize = 64;
        const int iterations = 350000;
        public static string GetSalt()
        {
            byte[] salt = RandomNumberGenerator.GetBytes(128 / 8); // divide by 8 to convert bits to bytes
            string b64Salt = Convert.ToBase64String(salt);

            return b64Salt;
        }

        public static string GetHash(string password, string b64salt)
        {
            byte[] salt = Convert.FromBase64String(b64salt);

            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA512,
                keySize);


            return Convert.ToHexString(hash);
        }
        public static int GetPin()
        {
            // alfanumeric pin
            //const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            //return new string(Enumerable.Repeat(chars, length)
            //.Select(s => s[random.Next(s.Length)]).ToArray());
            int _min = 1000;
            int _max = 9999;
            
            return RandomNumberGenerator.GetInt32(_min, _max);
        }

        public static string GetGuid()
        {
            return Guid.NewGuid().ToString();
        }

        public static bool VerifyHash(string? pwdHash, string? pwdSalt, string? password)
        {

            if(pwdHash == (GetHash(password, pwdSalt)))
            {

                return true;
            }

            else { return false; }
            
        }


        // DEVICE
        public static bool VerifyDevice(AuthenticationHeaderValue apiId, AuthenticationHeaderValue apikey)
        {

            Device device = RepoFactory.GetRepo().DeviceGet(0,null,apiId.ToString(),null); //popravi tenant
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
