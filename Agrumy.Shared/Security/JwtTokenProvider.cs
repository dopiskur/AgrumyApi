using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


namespace api.Security
{
    public class JwtTokenProvider
    {
        private static string? signKey = Config.secureKey;


        /// <summary>Roadmap #66: a user can hold several roles at once, so <paramref name="roles"/>
        /// becomes one <see cref="ClaimTypes.Role"/> claim per entry - ASP.NET Core's role
        /// authorization (<c>[Authorize(Roles=...)]</c>/<c>IsInRole</c>) natively checks across all
        /// of them, so every pre-#66 single-role check keeps working unmodified as long as the
        /// caller includes the legacy "admin"/"user" alias in the set (see
        /// api.Security.RoleNames.ImpliesLegacyAdmin and its callers).</summary>
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



        /// <summary>Every role claim on a valid token (roadmap #66 - a caller can hold several), or
        /// null if the token itself is invalid/expired/wrongly-signed. An empty (but non-null) list
        /// means the token validated but carried no role claims at all - callers must treat that as
        /// "no roles", not "check failed".</summary>
        public static IReadOnlyList<string>? ValidateToken(string token)
        {
            if (token == null)
                return null;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(signKey);
            try
            {
                tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    // Same Issuer/Audience CreateToken() stamps onto every token it mints (roadmap
                    // #48) - reads Config.jwtIssuer/jwtAudience here rather than assuming Program.cs's
                    // builder.Configuration values apply, since this runs inside Agrumy.Web's process
                    // too (LoginController.cs), a separate assembly with its own appsettings.json.
                    ValidateIssuer = true,
                    ValidIssuer = Config.jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = Config.jwtAudience,
                    // set clockskew to zero so tokens expire exactly at token expiration time (instead of 5 minutes later)
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                return jwtToken.Claims.Where(x => x.Type == "role").Select(x => x.Value).ToList();
            }
            catch
            {
                return null;
            }
        }
    }
}
