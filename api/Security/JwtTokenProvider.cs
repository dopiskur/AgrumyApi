using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using static Mysqlx.Expect.Open.Types.Condition.Types;

namespace api.Security
{
    public class JwtTokenProvider
    {
        private static string? signKey = Config.secureKey;


        public static string CreateToken(string secureKey, int expiration, string subject, string role, string tenantID)
        {
            // Get secret key bytes
            var tokenKey = Encoding.UTF8.GetBytes(secureKey);

            // Create a token descriptor (represents a token, kind of a "template" for token)
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Expires = DateTime.UtcNow.AddMinutes(expiration),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(tokenKey),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            if (!string.IsNullOrEmpty(subject))
            {
                tokenDescriptor.Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Name, subject),
                    new Claim(JwtRegisteredClaimNames.Sub, subject),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("TenantID", tenantID)
                });
            }

            // Create token using that descriptor, serialize it and return it
            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var serializedToken = tokenHandler.WriteToken(token);

            return serializedToken;
        }



        // OVO JE NAKNADNO DODANO!! NE RADI JOS KAK SPADA
        public static string? ValidateToken(string token)
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
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    // set clockskew to zero so tokens expire exactly at token expiration time (instead of 5 minutes later)
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                var value = jwtToken.Claims.First(x => x.Type == "role").Value;

                // return user id from JWT token if validation successful
                return value;
            }
            catch
            {
                // return null if validation fails
                return null;
            }
        }
    }
}
