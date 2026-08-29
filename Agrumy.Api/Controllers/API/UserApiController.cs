using api.Dal;
using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace api.Controllers.API
{
    [Route("api/User")]
    [ApiController]
    public class UserApiController : ControllerBase
    {
        private const int DefaultRoleId = 1;   // "regular user" group on an existing tenant
        private const bool DefaultUserEnabled = false;

        private static readonly string? SecureKey = Config.secureKey;

        private readonly ILogger<UserApiController> _logger;

        public UserApiController(ILogger<UserApiController> logger)
        {
            _logger = logger;
        }

        /// <summary>TenantID claim set at login (JwtTokenProvider.CreateToken) - null only if the claim is somehow missing.</summary>
        private int? GetCallerTenantId()
        {
            var identity = HttpContext.User.Identity as ClaimsIdentity;
            var claim = identity?.FindFirst("TenantID");
            return claim != null && int.TryParse(claim.Value, out var tenantId) ? tenantId : null;
        }

        private string? CallerRole() => (HttpContext.User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.Role)?.Value;

        /// <summary>Looks up a user + their password secret by email or username (whichever <paramref name="login"/> looks like).</summary>
        private static async Task<(User user, UserSecret secret)> LookupAsync(string? login)
        {
            var repo = RepoFactory.GetRepo();
            return FieldValidator.IsValidEmail(login)
                ? (await repo.UserGetAsync(null, login, null), await repo.UserSecretGetAsync(null, login, null))
                : (await repo.UserGetAsync(null, null, login), await repo.UserSecretGetAsync(null, null, login));
        }

        /// <summary>Maps a unique-constraint violation to a business response, or null if it isn't one.</summary>
        private ObjectResult? UniqueViolationResult(Exception e)
        {
            if (DbErrorResponse.Mentions(e, "email_UNIQUE"))
            {
                return StatusCode(500, "email already registered");
            }
            if (DbErrorResponse.Mentions(e, "Username_UNIQUE"))
            {
                return StatusCode(500, "username already registered");
            }
            return null;
        }

        private ObjectResult DbFailure(Exception e) =>
            StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(RepoFactory.GetRepo().ClassifyException(e)));

        // ---- registration / auth ---------------------------------------------------

        [HttpPost("Register")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<User>> UserRegistration([FromBody] UserRegistration value)
        {
            try
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

                if (!await RepoFactory.GetRepo().TenantGetAsync(value.TenantName))
                {
                    // Brand-new tenant: the registrant becomes its admin.
                    user.TenantID = await RepoFactory.GetRepo().TenantAddAsync(value.TenantName);
                    user.UserGroupID = 0;
                    user.Enabled = true;
                }
                else
                {
                    // Existing tenant: join as a regular, disabled user (not the default tenant 0 -
                    // that would merge unrelated tenants' users into one bucket).
                    user.TenantID = await RepoFactory.GetRepo().TenantGetIdAsync(value.TenantName);
                    user.UserGroupID = DefaultRoleId;
                    user.Enabled = DefaultUserEnabled;
                }

                await RepoFactory.GetRepo().UserAddAsync(user, userSecret);
                return Ok(user);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User registration failed");
                return UniqueViolationResult(e) ?? DbFailure(e);
            }
        }

        [HttpPost("Login")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<UserLoginResult>> UserLogin([FromBody] UserLogin value)
        {
            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                var (user, secret) = await LookupAsync(value.Login);

                if (!AuthenticationProvider.VerifyHash(secret.PwdHash, secret.PwdSalt, value.Password))
                {
                    return StatusCode(401, "Wrong username or password");
                }

                IList<UserRole> roles = await RepoFactory.GetRepo().UserRoleGetAsync();
                UserRole? role = roles.FirstOrDefault(m => m.IDUserRole == user.UserRoleID);
                if (role?.RoleName == null)
                {
                    return StatusCode(500, "User has no valid role assigned.");
                }

                string token = JwtTokenProvider.CreateToken(SecureKey, 120, user.Email, role.RoleName, user.TenantID.ToString());
                return Ok(new UserLoginResult { IDUser = user.IDUser, Email = user.Email, Token = token });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UserLogin failed");
                return DbFailure(ex);
            }
        }

        [HttpPost("ChangePassword")]
        public async Task<ActionResult<string>> UserSetPassword([FromBody] UserSetPassword value)
        {
            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                if (value.OldPassword == value.NewPassword)
                {
                    return StatusCode(403, "The new password must be different from the old password");
                }

                var (user, secret) = await LookupAsync(value.Login);

                if (!AuthenticationProvider.VerifyHash(secret.PwdHash, secret.PwdSalt, value.OldPassword))
                {
                    return StatusCode(401, "Wrong password");
                }

                secret.PwdSalt = AuthenticationProvider.GetSalt();
                secret.PwdHash = AuthenticationProvider.GetHash(value.NewPassword, secret.PwdSalt);

                return await RepoFactory.GetRepo().UserSetPasswordAsync(user.Email, secret)
                    ? Ok("Password changed successfully for: " + user.Email)
                    : StatusCode(403, "Password change failed for: " + user.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UserSetPassword failed");
                return DbFailure(ex);
            }
        }

        // ---- read ----------------------------------------------------------------

        [HttpGet("All")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<IList<User>>> UsersGet()
        {
            try
            {
                return Ok(await RepoFactory.GetRepo().UsersGetAsync(GetCallerTenantId()));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                return DbFailure(e);
            }
        }

        /// <summary>The caller's own record - looked up by the email in their JWT, so any authenticated user.</summary>
        [HttpGet("Self")]
        [Authorize]
        public async Task<ActionResult<User>> GetUserSelf()
        {
            try
            {
                return Ok(await RepoFactory.GetRepo().UserGetAsync(null, User.Identity?.Name, null));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                return DbFailure(e);
            }
        }

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<User>> UserGet(int idUser)
        {
            try
            {
                User user = await RepoFactory.GetRepo().UserGetAsync(idUser, null, null);
                if (user.TenantID != GetCallerTenantId())
                {
                    return StatusCode(403, "Target user belongs to a different tenant");
                }
                return Ok(user);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                return DbFailure(e);
            }
        }

        // ---- write -------------------------------------------------------------

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<string>> UserAdd([FromBody] UserAdd value)
        {
            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                var user = new User
                {
                    TenantID = GetCallerTenantId(), // payload's TenantID is ignored - admins only create in their own tenant
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

                await RepoFactory.GetRepo().UserAddAsync(user, userSecret);
                return Ok("User created successfully: " + user.Email);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User write operation failed");
                return UniqueViolationResult(e) ?? DbFailure(e);
            }
        }

        [HttpPut]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<bool>> UserUpdate([FromBody] UserUpdate value)
        {
            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                bool isAdmin = CallerRole() == "admin";
                User user = await RepoFactory.GetRepo().UserGetAsync(value.IDUser, null, null);

                if (!isAdmin && value.Email != User.Identity?.Name) // a plain user may only edit their own record
                {
                    return Unauthorized();
                }
                if (user.TenantID != GetCallerTenantId())
                {
                    return StatusCode(403, "Target user belongs to a different tenant");
                }

                if (value.Password != null)
                {
                    var secret = await RepoFactory.GetRepo().UserSecretGetAsync(value.IDUser, null, null);
                    secret.PwdSalt = AuthenticationProvider.GetSalt();
                    secret.PwdHash = AuthenticationProvider.GetHash(value.Password, secret.PwdSalt);
                    await RepoFactory.GetRepo().UserSetPasswordAsync(user.Email, secret);
                }

                if (value.Email != null) { user.Email = value.Email; }
                if (value.Username != null) { user.Username = value.Username; }
                if (value.FirstName != null) { user.FirstName = value.FirstName; }
                if (value.LastName != null) { user.LastName = value.LastName; }
                if (value.Phone != null) { user.Phone = value.Phone; }
                if (value.UserGroupID != null && isAdmin) { user.UserGroupID = value.UserGroupID; } // role change: admin only
                if (value.Enabled != null && isAdmin) { user.Enabled = value.Enabled; }             // enable/disable: admin only

                await RepoFactory.GetRepo().UserUpdateAsync(user);
                return Ok(true);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User write operation failed");
                return UniqueViolationResult(e) ?? DbFailure(e);
            }
        }

        [HttpDelete]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<string>> Delete(int? idUser)
        {
            try
            {
                if (idUser is null or <= 1) // ids 0 and 1 are the protected default accounts
                {
                    return Unauthorized("Deleting default user is not allowed");
                }

                User targetUser = await RepoFactory.GetRepo().UserGetAsync(idUser, null, null);
                if (targetUser.TenantID != GetCallerTenantId())
                {
                    return StatusCode(403, "Target user belongs to a different tenant");
                }

                return await RepoFactory.GetRepo().UserDeleteAsync(idUser)
                    ? Ok("User deleted")
                    : NotFound("User not found");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User write operation failed");
                return DbFailure(e);
            }
        }

        [HttpGet("Roles")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<IEnumerable<UserRole>>> UserRoleGet() =>
            Ok(await RepoFactory.GetRepo().UserRoleGetAsync());

        // ---- groups ----------------------------------------------------------

        [HttpGet("Group/All")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<IEnumerable<UserGroup>>> UserGroupsGet()
        {
            try
            {
                return Ok(await RepoFactory.GetRepo().UserGroupsGetAsync());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                return DbFailure(e);
            }
        }

        [HttpGet("Group")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<UserGroup>> UserGroupGet(int? idUserGroup)
        {
            try
            {
                return Ok(await RepoFactory.GetRepo().UserGroupGetAsync(idUserGroup));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                return DbFailure(e);
            }
        }

        [HttpPost("Group")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<bool>> UserGroupAdd(UserGroup userGroup)
        {
            try
            {
                await RepoFactory.GetRepo().UserGroupAddAsync(userGroup);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "UserGroupAdd failed for group {GroupName}", userGroup?.GroupName);
                return DbFailure(e);
            }
        }

        [HttpDelete("Group")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<bool>> UserGroupDelete(int? idUserGroup)
        {
            try
            {
                await RepoFactory.GetRepo().UserGroupDeleteAsync(idUserGroup);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "UserGroupDelete failed for group {IdUserGroup}", idUserGroup);
                return DbFailure(e);
            }
        }
    }
}
