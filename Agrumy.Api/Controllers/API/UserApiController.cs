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

        private static readonly string? SecureKey = Config.secureKey;

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

            string token = JwtTokenProvider.CreateToken(SecureKey, 120, user.Email, role.RoleName, user.TenantID.ToString());
            return Ok(new UserLoginResult { IDUser = user.IDUser, Email = user.Email, Token = token });
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
            User? user = await Repo.UserGetAsync(null, User.Identity?.Name, null);
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
                var secret = await Repo.UserSecretGetAsync(value.IDUser, null, null) ?? new UserSecret();
                secret.PwdSalt = AuthenticationProvider.GetSalt();
                secret.PwdHash = AuthenticationProvider.GetHash(value.Password, secret.PwdSalt);
                await Repo.UserSetPasswordAsync(user.Email, secret);
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
