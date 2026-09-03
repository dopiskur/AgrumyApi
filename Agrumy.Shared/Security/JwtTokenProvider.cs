using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


namespace api.Security
{
    public partial class JwtTokenProvider
    {
        /// <summary>Static bridge into ILogger since this class has no DI reach; each host assigns it once at startup. Null (e.g. unit tests) means rejections stay silent.</summary>
        public static ILogger? Logger { get; set; }

        [LoggerMessage(Level = LogLevel.Warning, Message = "JWT rejected: expired.")]
        private static partial void LogTokenExpired(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "JWT rejected: invalid signature - key value or key-encoding mismatch between issuer and validator.")]
        private static partial void LogInvalidSignature(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "JWT rejected: {ExceptionType} - malformed or otherwise invalid token.")]
        private static partial void LogMalformedToken(ILogger logger, string exceptionType);


        /// <summary>A user can hold several roles at once, so <paramref name="roles"/> becomes one <see cref="ClaimTypes.Role"/> claim per entry; the caller must include the legacy "admin"/"user" alias in the set for old single-role checks to keep working (see api.Security.RoleNames.ImpliesLegacyAdmin).</summary>
        public static string CreateToken(string secureKey, int expiration, string subject, IEnumerable<string> roles, string tenantID)
        {
            var tokenKey = Encoding.UTF8.GetBytes(secureKey);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Issuer = Config.jwtIssuer,
                Audience = Config.jwtAudience,
                Expires = DateTime.UtcNow.AddMinutes(expiration),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(tokenKey),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            if (!string.IsNullOrEmpty(subject))
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, subject),
                    new(JwtRegisteredClaimNames.Sub, subject),
                    new("TenantID", tenantID),
                };
                claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
                tokenDescriptor.Subject = new ClaimsIdentity(claims);
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var serializedToken = tokenHandler.WriteToken(token);

            return serializedToken;
        }



        /// <summary>Every role claim on a valid token, or null if the token is invalid/expired/wrongly-signed. An empty (non-null) list means the token validated but carried no roles — callers must treat that as "no roles", not "check failed".</summary>
        public static IReadOnlyList<string>? ValidateToken(string token) => ValidateToken(token, Config.secureKey);

        /// <summary>Key-parameterized overload for tests driving the real validation path with a chosen key; production callers never pass a key (the single-arg overload reads Config.secureKey live rather than a cached field, since it must reflect Config.Init() having run at host startup).</summary>
        public static IReadOnlyList<string>? ValidateToken(string token, string? secureKey)
        {
            if (token == null || secureKey == null)
                return null;

            var tokenHandler = new JwtSecurityTokenHandler();
            // MUST be the same encoding CreateToken (and Agrumy.Api's AddJwtBearer) uses to derive key bytes, or a SecureKey character above U+007F silently produces two different keys.
            var key = Encoding.UTF8.GetBytes(secureKey);
            try
            {
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    // Reads Config.jwtIssuer/jwtAudience directly (not builder.Configuration) since this also runs inside Agrumy.Web, a separate assembly with its own appsettings.json.
                    ValidateIssuer = true,
                    ValidIssuer = Config.jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = Config.jwtAudience,
                    // Default ClockSkew is 5 minutes; zero here means tokens expire exactly at their stated expiration.
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                return jwtToken.Claims.Where(x => x.Type == "role").Select(x => x.Value).ToList();
            }
            // Every failure returns the same null result (callers depend on that); the cause still reaches the log via the distinct catch blocks below.
            catch (SecurityTokenExpiredException)
            {
                if (Logger is not null) { LogTokenExpired(Logger); }
                return null;
            }
            catch (SecurityTokenInvalidSignatureException)
            {
                if (Logger is not null) { LogInvalidSignature(Logger); }
                return null;
            }
            catch (Exception ex)
            {
                if (Logger is not null) { LogMalformedToken(Logger, ex.GetType().Name); }
                return null;
            }
        }
    }
}
