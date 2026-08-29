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
            if (pwdHash is null || pwdSalt is null || password is null)
            {
                return false;
            }

            // GetHash returns an upper-case hex string; compare it to the stored hash in
            // constant time over the raw string bytes (behaviour is identical to the old
            // case-sensitive `==`, just without the early-exit timing side channel).
            byte[] storedBytes = Encoding.UTF8.GetBytes(pwdHash);
            byte[] computedBytes = Encoding.UTF8.GetBytes(GetHash(password, pwdSalt));

            // FixedTimeEquals requires equal-length inputs; a length mismatch is simply a
            // non-match and must not throw or leak information.
            if (storedBytes.Length != computedBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(storedBytes, computedBytes);
        }

        // NOTE: device apiId/apiKey verification lives in api.Security.DeviceAuth (Agrumy.Api)
        // (Agrumy.Api) because it needs the data-access layer, which this shared assembly must not
        // reference.
    }
}
