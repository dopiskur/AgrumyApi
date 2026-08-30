using System.Security.Cryptography;
using System.Text;
using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers.API
{
    [Route("api/User")]
    public class UserApiController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
        private const int DefaultRoleId = 1;   // "regular user" group on an existing tenant
        private const bool DefaultUserEnabled = false;
        private const int AccessTokenMinutes = 120;
        private const int RefreshTokenDays = 30;

        private static readonly string? SecureKey = Config.secureKey;

        /// <summary>New opaque refresh token plus the hash that's actually stored - the plaintext
        /// never touches the DB.</summary>
        private static (string Plaintext, string Hash) GenerateRefreshToken()
        {
            string plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            return (plaintext, HashRefreshToken(plaintext));
        }

        private static string HashRefreshToken(string plaintext) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));

        /// <summary>Looks up a user + their password secret by email or username (whichever <paramref name="login"/> looks like); either element is null when nothing matches.</summary>
        private async Task<(User? user, UserSecret? secret)> LookupAsync(string? login) =>
            FieldValidator.IsValidEmail(login)
                ? (await Repo.UserGetAsync(null, login, null), await Repo.UserSecretGetAsync(null, login, null))
                : (await Repo.UserGetAsync(null, null, login), await Repo.UserSecretGetAsync(null, null, login));

        // ---- registration / auth ---------------------------------------------------

        [HttpPost("Register")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<User>> UserRegistration([FromBody] UserRegistration value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            var user = new User
            {
                Email = value.Email,
                Username = value.Username,
                FirstName = value.FirstName,
                LastName = value.LastName,
                Phone = value.Phone,
            };

            var userSecret = new UserSecret { PwdSalt = AuthenticationProvider.GetSalt() };
            userSecret.PwdHash = AuthenticationProvider.GetHash(value.Password, userSecret.PwdSalt);

            if (!await Repo.TenantGetAsync(value.TenantName))
            {
                // Brand-new tenant: the registrant becomes its admin.
                user.TenantID = await Repo.TenantAddAsync(value.TenantName);
                user.UserGroupID = 0;
                user.Enabled = true;
            }
            else
            {
                // Existing tenant: join as a regular, disabled user (not the default tenant 0 -
                // that would merge unrelated tenants' users into one bucket).
                user.TenantID = await Repo.TenantGetIdAsync(value.TenantName);
                user.UserGroupID = DefaultRoleId;
                user.Enabled = DefaultUserEnabled;
            }

            await Repo.UserAddAsync(user, userSecret);
            return Ok(user);
        }

        [HttpPost("Login")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<UserLoginResult>> UserLogin([FromBody] UserLogin value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            var (user, secret) = await LookupAsync(value.Login);
            if (user is null || secret is null ||
                !AuthenticationProvider.VerifyHash(secret.PwdHash, secret.PwdSalt, value.Password))
            {
                return StatusCode(401, "Wrong username or password");
            }

            IList<UserRole> roles = await Repo.UserRoleGetAsync();
            UserRole? role = roles.FirstOrDefault(m => m.IDUserRole == user.UserRoleID);
            if (role?.RoleName == null)
            {
                return StatusCode(500, "User has no valid role assigned.");
            }

            string token = JwtTokenProvider.CreateToken(SecureKey, AccessTokenMinutes, user.Email, role.RoleName, user.TenantID.ToString());
            var (refreshToken, refreshTokenHash) = GenerateRefreshToken();
            await Repo.RefreshTokenAddAsync(user.IDUser!.Value, refreshTokenHash, DateTime.UtcNow.AddDays(RefreshTokenDays));

            return Ok(new UserLoginResult { IDUser = user.IDUser, Email = user.Email, Token = token, RefreshToken = refreshToken });
        }

        /// <summary>Redeems a refresh token for a new access token, rotating the refresh token in the
        /// same call (single-use). Anonymous by design - the refresh token itself is the credential,
        /// same model as the login endpoint it sits next to.</summary>
        [HttpPost("RefreshToken")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<UserLoginResult>> RefreshToken([FromBody] RefreshTokenRequest value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            string incomingHash = HashRefreshToken(value.RefreshToken!);
            RefreshTokenInfo? stored = await Repo.RefreshTokenGetAsync(incomingHash);
            if (stored is null)
            {
                return StatusCode(401, "Unknown refresh token");
            }
            if (stored.RevokedAt is not null)
            {
                // This exact token was already rotated (or explicitly revoked) - someone presenting
                // it again means it leaked. Kill every session for this user, not just this one.
                await Repo.RefreshTokenRevokeAllForUserAsync(stored.UserID);
                return StatusCode(401, "Refresh token already used; all sessions for this user were revoked.");
            }
            if (stored.ExpiresAt < DateTime.UtcNow)
            {
                return StatusCode(401, "Refresh token expired");
            }

            User? user = await Repo.UserGetAsync(stored.UserID, null, null);
            if (user is null)
            {
                return StatusCode(401, "User no longer exists");
            }

            IList<UserRole> roles = await Repo.UserRoleGetAsync();
            UserRole? role = roles.FirstOrDefault(m => m.IDUserRole == user.UserRoleID);
            if (role?.RoleName == null)
            {
                return StatusCode(500, "User has no valid role assigned.");
            }

            var (newRefreshToken, newRefreshTokenHash) = GenerateRefreshToken();
            await Repo.RefreshTokenRotateAsync(incomingHash, newRefreshTokenHash, DateTime.UtcNow.AddDays(RefreshTokenDays));

            string newAccessToken = JwtTokenProvider.CreateToken(SecureKey, AccessTokenMinutes, user.Email, role.RoleName, user.TenantID.ToString());
            return Ok(new UserLoginResult { IDUser = user.IDUser, Email = user.Email, Token = newAccessToken, RefreshToken = newRefreshToken });
        }

        /// <summary>Explicit logout: kills one refresh token so it can't be redeemed later. Idempotent
        /// and always 200 - a client logging out must not be blocked by an already-gone token.</summary>
        [HttpPost("RevokeRefreshToken")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult> RevokeRefreshToken([FromBody] RefreshTokenRequest value)
        {
            if (!string.IsNullOrWhiteSpace(value.RefreshToken))
            {
                await Repo.RefreshTokenRevokeAsync(HashRefreshToken(value.RefreshToken));
            }
            return Ok();
        }

        [HttpPost("ChangePassword")]
        public async Task<ActionResult<string>> UserSetPassword([FromBody] UserSetPassword value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            if (value.OldPassword == value.NewPassword)
            {
                return StatusCode(403, "The new password must be different from the old password");
            }

            var (user, secret) = await LookupAsync(value.Login);
            if (user is null || secret is null ||
                !AuthenticationProvider.VerifyHash(secret.PwdHash, secret.PwdSalt, value.OldPassword))
            {
                return StatusCode(401, "Wrong password");
            }

            secret.PwdSalt = AuthenticationProvider.GetSalt();
            secret.PwdHash = AuthenticationProvider.GetHash(value.NewPassword, secret.PwdSalt);

            return await Repo.UserSetPasswordAsync(user.Email, secret)
                ? Ok("Password changed successfully for: " + user.Email)
                : StatusCode(403, "Password change failed for: " + user.Email);
        }

        // ---- read ----------------------------------------------------------------

        [HttpGet("All")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<IList<User>>> UsersGet() =>
            Ok(await Repo.UsersGetAsync(CallerTenantId));

        /// <summary>The caller's own record - looked up by the email in their JWT, so any authenticated user.</summary>
        [HttpGet("Self")]
        [Authorize]
        public async Task<ActionResult<User>> GetUserSelf()
        {
            string? name = User.Identity?.Name;
            if (string.IsNullOrEmpty(name))
            {
                return Unauthorized();
            }
            User? user = await Repo.UserGetAsync(null, name, null);
            return user is null ? NotFound() : Ok(user);
        }

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<User>> UserGet(int idUser)
        {
            User? user = await Repo.UserGetAsync(idUser, null, null);
            if (user is null)
            {
                return NotFound();
            }
            return user.TenantID != CallerTenantId
                ? StatusCode(403, "Target user belongs to a different tenant")
                : Ok(user);
        }

        // ---- write -------------------------------------------------------------

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<string>> UserAdd([FromBody] UserAdd value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            var user = new User
            {
                TenantID = CallerTenantId, // payload's TenantID is ignored - admins only create in their own tenant
                UserGroupID = value.UserGroupID,
                Email = value.Email,
                Username = value.Username,
                FirstName = value.FirstName,
                LastName = value.LastName,
                Phone = value.Phone,
                Enabled = value.Enabled,
            };

            var userSecret = new UserSecret { PwdSalt = AuthenticationProvider.GetSalt() };
            userSecret.PwdHash = AuthenticationProvider.GetHash(value.Password, userSecret.PwdSalt);

            await Repo.UserAddAsync(user, userSecret);
            return Ok("User created successfully: " + user.Email);
        }

        [HttpPut]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<bool>> UserUpdate([FromBody] UserUpdate value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            bool isAdmin = CallerRole == "admin";
            User? user = await Repo.UserGetAsync(value.IDUser, null, null);
            if (user is null)
            {
                return NotFound();
            }

            if (!isAdmin && value.Email != User.Identity?.Name) // a plain user may only edit their own record
            {
                return Unauthorized();
            }
            if (user.TenantID != CallerTenantId)
            {
                return StatusCode(403, "Target user belongs to a different tenant");
            }

            if (value.Password != null)
            {
                // Admin/self edit - no old-password check here, so the fresh salt+hash fully
                // replace whatever was there; no need to read the current secret first.
                string salt = AuthenticationProvider.GetSalt();
                await Repo.UserSetPasswordAsync(user.Email, new UserSecret
                {
                    PwdSalt = salt,
                    PwdHash = AuthenticationProvider.GetHash(value.Password, salt),
                });
            }

            if (value.Email != null) { user.Email = value.Email; }
            if (value.Username != null) { user.Username = value.Username; }
            if (value.FirstName != null) { user.FirstName = value.FirstName; }
            if (value.LastName != null) { user.LastName = value.LastName; }
            if (value.Phone != null) { user.Phone = value.Phone; }
            if (value.UserGroupID != null && isAdmin) { user.UserGroupID = value.UserGroupID; } // role change: admin only
            if (value.Enabled != null && isAdmin) { user.Enabled = value.Enabled; }             // enable/disable: admin only

            await Repo.UserUpdateAsync(user);
            return Ok(true);
        }

        [HttpDelete]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<string>> Delete(int? idUser)
        {
            if (idUser is null or <= 1) // ids 0 and 1 are the protected default accounts
            {
                return Unauthorized("Deleting default user is not allowed");
            }

            User? targetUser = await Repo.UserGetAsync(idUser, null, null);
            if (targetUser is null)
            {
                return NotFound("User not found");
            }
            if (targetUser.TenantID != CallerTenantId)
            {
                return StatusCode(403, "Target user belongs to a different tenant");
            }

            return await Repo.UserDeleteAsync(idUser) ? Ok("User deleted") : NotFound("User not found");
        }

        [HttpGet("Roles")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<IEnumerable<UserRole>>> UserRoleGet() =>
            Ok(await Repo.UserRoleGetAsync());

        // ---- groups ----------------------------------------------------------

        [HttpGet("Group/All")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<IEnumerable<UserGroup>>> UserGroupsGet() =>
            Ok(await Repo.UserGroupsGetAsync());

        [HttpGet("Group")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<UserGroup>> UserGroupGet(int? idUserGroup)
        {
            UserGroup? group = await Repo.UserGroupGetAsync(idUserGroup);
            return group is null ? NotFound() : Ok(group);
        }

        [HttpPost("Group")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<bool>> UserGroupAdd(UserGroup userGroup)
        {
            await Repo.UserGroupAddAsync(userGroup);
            return true;
        }

        [HttpDelete("Group")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<bool>> UserGroupDelete(int? idUserGroup)
        {
            await Repo.UserGroupDeleteAsync(idUserGroup);
            return true;
        }
    }
}
