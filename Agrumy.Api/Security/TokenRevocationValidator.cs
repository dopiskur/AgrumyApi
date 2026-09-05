using System.IdentityModel.Tokens.Jwt;
using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace api.Security
{
    /// AddJwtBearer's OnTokenValidated hook - rejects a structurally valid, unexpired token if the caller's password changed or account was disabled after it was issued. See api.Security.TokenRevocationCheck for the actual decision.
    public static class TokenRevocationValidator
    {
        public static async Task ValidateAsync(TokenValidatedContext context)
        {
            if (context.Principal?.Identity?.Name is not string email ||
                context.SecurityToken is not JwtSecurityToken jwt)
            {
                return;
            }

            IRepository repo = context.HttpContext.RequestServices.GetRequiredService<IRepository>();
            User? user = await repo.UserGetAsync(null, email, null);
            if (user is not null && TokenRevocationCheck.IsRevoked(jwt.IssuedAt, user.TokensValidAfterUtc))
            {
                context.Fail("Token revoked - password changed or account disabled.");
            }
        }
    }
}
