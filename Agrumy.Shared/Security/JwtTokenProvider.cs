using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


namespace api.Security
{
    public class JwtTokenProvider
    {
        private static string? signKey = Config.secureKey;


        public static string CreateToken(string secureKey, int expiration, string subject, string role, string tenantID)
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
                tokenDescriptor.Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Name, subject),
                    new Claim(JwtRegisteredClaimNames.Sub, subject),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("TenantID", tenantID)
                });
            }

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var serializedToken = tokenHandler.WriteToken(token);

            return serializedToken;
        }



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
                var value = jwtToken.Claims.First(x => x.Type == "role").Value;

                return value;
            }
            catch
            {
                return null;
            }
        }
    }
}
