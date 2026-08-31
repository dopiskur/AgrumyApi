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

        // Roadmap #73: device credentials (ApiKey, session apiAuth) need a CSPRNG source -
        // Guid.NewGuid() is documented by Microsoft as not guaranteed cryptographically secure,
        // only "sufficiently random" for identifiers. Same RandomNumberGenerator source as
        // GetSalt() above; 256 bits (vs. GetSalt()'s 128) because these values ARE the credential
        // itself, not combined with a password. Base64 output is a valid HTTP header value as-is
        // (ApiKey/apiAuth are sent in custom headers, not URL segments, so no URL-safe encoding
        // is needed).
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
        // Roadmap #70: 32-char alphabet (uppercase minus O/I, digits minus 0/1) x 6 chars =
        // 32^6 ~ 1.07e9 possibilities - with the 20/min-per-IP rate limit on Register, even a
        // 1000-IP distributed brute force covers ~1.3% of the space inside the 24h validity
        // window. The excluded chars kill the 0/O and 1/I/l read-aloud ambiguity.
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

        /// <summary>Roadmap #70: a stored PIN counts only while unexpired (a null expiry means
        /// "never issued under the current scheme" - e.g. a legacy 4-digit PIN carried over by the
        /// devicepin migration - and is deliberately treated as invalid, not as valid-forever);
        /// the comparison is case-insensitive because the captive-portal field is free text, and
        /// fixed-time for the same reason VerifyHash is.</summary>
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
