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
            byte[] salt = RandomNumberGenerator.GetBytes(128 / 8);
            string b64Salt = Convert.ToBase64String(salt);

            return b64Salt;
        }

        // CSPRNG (not Guid.NewGuid, which MS docs don't guarantee cryptographically secure); 256 bits since this value IS the credential itself, not combined with a password.
        public static string GetSecureToken()
        {
            byte[] token = RandomNumberGenerator.GetBytes(256 / 8);
            return Convert.ToBase64String(token);
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
        // Excludes 0/O and 1/I/l to kill read-aloud ambiguity.
        private const string PinAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        public const int PinLength = 6;
        public const int PinValidHours = 24;

        public static string GetPin()
        {
            char[] pin = new char[PinLength];
            for (int i = 0; i < pin.Length; i++)
            {
                pin[i] = PinAlphabet[RandomNumberGenerator.GetInt32(PinAlphabet.Length)];
            }
            return new string(pin);
        }

        /// A null expiry is treated as invalid, never as valid-forever; comparison is case-insensitive (free-text captive-portal field) and fixed-time (same reason as VerifyHash).
        public static bool VerifyPin(string? storedPin, DateTime? expiresAtUtc, string? providedPin)
        {
            if (string.IsNullOrWhiteSpace(storedPin) || string.IsNullOrWhiteSpace(providedPin) ||
                expiresAtUtc is null || expiresAtUtc < DateTime.UtcNow)
            {
                return false;
            }

            byte[] stored = Encoding.UTF8.GetBytes(storedPin.Trim().ToUpperInvariant());
            byte[] provided = Encoding.UTF8.GetBytes(providedPin.Trim().ToUpperInvariant());
            if (stored.Length != provided.Length)
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(stored, provided);
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

            byte[] storedBytes = Encoding.UTF8.GetBytes(pwdHash);
            byte[] computedBytes = Encoding.UTF8.GetBytes(GetHash(password, pwdSalt));

            // FixedTimeEquals requires equal-length inputs; a length mismatch must be a non-match, not a throw.
            if (storedBytes.Length != computedBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(storedBytes, computedBytes);
        }

        // Device apiId/apiKey verification lives in api.Security.DeviceAuth (Agrumy.Api) since it needs the data-access layer, which this shared assembly must not reference.
    }
}
