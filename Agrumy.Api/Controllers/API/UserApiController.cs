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
        private static string? secureKey = Config.secureKey;

        private readonly ILogger<UserApiController> _logger;

        public UserApiController(ILogger<UserApiController> logger)
        {
            _logger = logger;
        }

        // CONSTANTS
        private readonly int? DEFAULT_TENANTID = 0;
        private readonly int? DEFAULT_ROLEID = 1;
        private readonly bool? DEFAULT_USER_ENABLED = false;
        private readonly bool? TENANT_ENABLED = false;



        // User registration
        [HttpPost("Register")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<UserRegistration>> UserRegistration([FromBody] UserRegistration value)
        {
            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                User user = new User();


                user.TenantID = DEFAULT_TENANTID; // default tenant value
                user.UserGroupID = DEFAULT_ROLEID; // set as user on existing tenant
                user.Enabled = DEFAULT_USER_ENABLED; // user disabled by default
                user.Email = value.Email;
                user.Username = value.Username;
                user.FirstName = value.FirstName;
                user.LastName = value.LastName;
                user.Phone = value.Phone;

                UserSecret userSecret = new UserSecret();
                userSecret.PwdSalt = AuthenticationProvider.GetSalt();
                userSecret.PwdHash = AuthenticationProvider.GetHash(value.Password, userSecret.PwdSalt);


                // check tenant name
                if (TENANT_ENABLED == true)
                {
                    if (!await RepoFactory.GetRepo().TenantGetAsync(value.TenantName))
                    {
                        user.TenantID = await RepoFactory.GetRepo().TenantAddAsync(value.TenantName);
                        user.UserGroupID = 0; // set as admin on new tenant
                        user.Enabled = true; // set as enabled user
                    }
                    else
                    {
                        // Tenant already exists: join it as a regular, disabled user instead of
                        // silently falling through to DEFAULT_TENANTID (0) - that would have
                        // merged unrelated tenants' users into the same "default" tenant bucket.
                        user.TenantID = await RepoFactory.GetRepo().TenantGetIdAsync(value.TenantName);
                        user.UserGroupID = DEFAULT_ROLEID; // regular user, not admin
                        user.Enabled = DEFAULT_USER_ENABLED; // waits for that tenant's admin to enable them
                    }
                }


                await RepoFactory.GetRepo().UserAddAsync(user, userSecret);

                return Ok(user);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User write operation failed");

                // Unique-constraint hits are a meaningful business response, not an internal detail.
                if (e.Message.Contains("email_UNIQUE"))
                {
                    return StatusCode(500, "email already registered");
                }

                if (e.Message.Contains("Username_UNIQUE"))
                {
                    return StatusCode(500, "username already registered");
                }

                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }

        }

        // User login
        [HttpPost("Login")]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<UserLogin>> UserLogin([FromBody] UserLogin value)
        {
            //AuthProvider.VerifyPassword(value.Email,value.Password);

            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                User user = new User();
                UserSecret userSecret = new UserSecret();

                if (FieldValidator.IsValidEmail(value.Login))
                {
                    user = await RepoFactory.GetRepo().UserGetAsync(null, value.Login, null);
                    userSecret = await RepoFactory.GetRepo().UserSecretGetAsync(null, value.Login, null);
                }
                else
                {
                    user = await RepoFactory.GetRepo().UserGetAsync(null, null, value.Login);
                    userSecret = await RepoFactory.GetRepo().UserSecretGetAsync(null, null, value.Login);
                }

                if (AuthenticationProvider.VerifyHash(userSecret.PwdHash, userSecret.PwdSalt, value.Password))
                {

                    IList<UserRole> roles = await RepoFactory.GetRepo().UserRoleGetAsync();
                    string roleName = roles.First(m => m.IDUserRole == user.UserRoleID).RoleName;

                    var serializedToken = JwtTokenProvider.CreateToken(secureKey, 120, user.Email, roleName, user.TenantID.ToString());


                    UserLoginResult userLoginResult = new UserLoginResult();
                    userLoginResult.IDUser = user.IDUser;
                    userLoginResult.Email = user.Email;
                    userLoginResult.Token = serializedToken;

                    return Ok(userLoginResult);
                }
                {
                    return StatusCode(401, "Wrong username or password");
                }


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UserLogin failed");
                var kind = RepoFactory.GetRepo().ClassifyException(ex);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        // Change password
        [HttpPost("ChangePassword")]
        public async Task<ActionResult<UserSetPassword>> UserSetPassword([FromBody] UserSetPassword value)
        {

            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                if (value.OldPassword == value.NewPassword)
                {
                    return StatusCode(403, "The new password must be different from the old password");
                }

                User user = new User();
                UserSecret userSecret = new UserSecret();

                if (FieldValidator.IsValidEmail(value.Login))
                {
                    user = await RepoFactory.GetRepo().UserGetAsync(null, value.Login, null);
                    userSecret = await RepoFactory.GetRepo().UserSecretGetAsync(null, value.Login, null);
                }
                else
                {
                    user = await RepoFactory.GetRepo().UserGetAsync(null, null, value.Login);
                    userSecret = await RepoFactory.GetRepo().UserSecretGetAsync(null, null, value.Login);
                }

                if (AuthenticationProvider.VerifyHash(userSecret.PwdHash, userSecret.PwdSalt, value.OldPassword))
                {
                    userSecret.PwdSalt = AuthenticationProvider.GetSalt();
                    userSecret.PwdHash = AuthenticationProvider.GetHash(value.NewPassword, userSecret.PwdSalt);

                    if (await RepoFactory.GetRepo().UserSetPasswordAsync(user.Email, userSecret))
                    {
                        return Ok("Password changed successfully for: " + user.Email);
                    }
                    else
                    {
                        return StatusCode(403, "Password change failed for: +user.Email");
                    }

                }
                {
                    return StatusCode(401, "Wrong password");
                }


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UserSetPassword failed");
                var kind = RepoFactory.GetRepo().ClassifyException(ex);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }


        // Get all users, as admin
        [HttpGet("All")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<IList<User>>> UsersGet()
        {

            try
            {
                IList<User> users = new List<User>();
                users = await RepoFactory.GetRepo().UsersGetAsync(DEFAULT_TENANTID);

                return Ok(users);

            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }

        }


        // get user by self
        [HttpGet("Self")]
        [Authorize(Roles = "admin")]
        //[Authorize(Roles = "admin, user")]
        public async Task<ActionResult<User>> GetUserSelf()
        {
            try
            {
                var identity = HttpContext.User.Identity as ClaimsIdentity;
                User user;
                user = await RepoFactory.GetRepo().UserGetAsync(null, identity.Name, null);

                return Ok(user);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }

        }


        // get user by ID
        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<User>> UserGet(int idUser)
        {
            try
            {

                var identity = HttpContext.User.Identity as ClaimsIdentity;

                if (!(identity.FindFirst(ClaimTypes.Role).Value == "admin"))
                {
                    return Unauthorized();
                }


                User user = await RepoFactory.GetRepo().UserGetAsync(idUser, null, null);

                return Ok(user);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }

        }


        // Create user as admin
        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<UserAdd>> UserAdd([FromBody] UserAdd? value)
        {
            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                User user = new User();

                user.TenantID = value.TenantID;
                user.UserGroupID = value.UserGroupID;
                user.Email = value.Email;
                user.Username = value.Username;
                user.FirstName = value.FirstName;
                user.LastName = value.LastName;
                user.Phone = value.Phone;
                user.Enabled = value.Enabled;


                UserSecret userSecret = new UserSecret();
                userSecret.PwdSalt = AuthenticationProvider.GetSalt();
                userSecret.PwdHash = AuthenticationProvider.GetHash(value.Password, userSecret.PwdSalt);


                await RepoFactory.GetRepo().UserAddAsync(user, userSecret);

                return Ok("User created successfully: " + user.Email);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User write operation failed");

                // Unique-constraint hits are a meaningful business response, not an internal detail.
                if (e.Message.Contains("email_UNIQUE"))
                {
                    return StatusCode(500, "email already registered");
                }

                if (e.Message.Contains("Username_UNIQUE"))
                {
                    return StatusCode(500, "username already registered");
                }

                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }

        }

        // Update users ad admin, or self as user
        [HttpPut]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<bool>> UserUpdate([FromBody] UserUpdate value)
        {
            try
            {
                bool result = false;
                if (!ModelState.IsValid) { return BadRequest(ModelState); }
                var identity = HttpContext.User.Identity as ClaimsIdentity;

                User user = await RepoFactory.GetRepo().UserGetAsync(value.IDUser, null, null);

                if (identity.FindFirst(ClaimTypes.Role).Value == "user" && value.Email != identity.Name) // if user, jwt token name must be equal to email identity
                {
                    return Unauthorized();
                }


                // check for password change
                UserSecret userSecret = await RepoFactory.GetRepo().UserSecretGetAsync(value.IDUser, null, null);
                if (value.Password != null)
                {

                    userSecret.PwdSalt = AuthenticationProvider.GetSalt();
                    userSecret.PwdHash = AuthenticationProvider.GetHash(value.Password, userSecret.PwdSalt);
                    await RepoFactory.GetRepo().UserSetPasswordAsync(user.Email, userSecret);
                }

                // if (value.TenantID != null) { user.TenantID = value.TenantID; } // ovo ostavljamo za iducu iteraciju
                if (value.Email != null) { user.Email = value.Email; }
                if (value.Username != null) { user.Username = value.Username; }
                if (value.FirstName != null) { user.FirstName = value.FirstName; }
                if (value.LastName != null) { user.LastName = value.LastName; }
                if (value.Phone != null) { user.Phone = value.Phone; }
                if (value.UserGroupID != null && identity.FindFirst(ClaimTypes.Role).Value == "admin") { user.UserGroupID = value.UserGroupID; } // can change roleid only if admin
                if (value.Enabled != null && identity.FindFirst(ClaimTypes.Role).Value == "admin") { user.Enabled = value.Enabled; } // can change enabled status only if admin


                await RepoFactory.GetRepo().UserUpdateAsync(user);

                return Ok(result=true);


            }
            catch (Exception e)
            {
                _logger.LogError(e, "User write operation failed");

                // Unique-constraint hits are a meaningful business response, not an internal detail.
                if (e.Message.Contains("email_UNIQUE"))
                {
                    return StatusCode(500, "email already registered");
                }

                if (e.Message.Contains("Username_UNIQUE"))
                {
                    return StatusCode(500, "username already registered");
                }

                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }

        }

        // DELETE api/<UserController>/5
        [HttpDelete]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<string>> Delete(int? idUser)
        {
            try
            {


                if (idUser > 1) // preventing deletion of admin
                {
                    if (await RepoFactory.GetRepo().UserDeleteAsync(idUser))
                    {
                        return Ok("User deleted");
                    };
                    return NotFound("User not found");
                }


                return Unauthorized("Deleting default user is not allowed");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }

        }


        // User Role List
        [HttpGet("Roles")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<string>> UserRoleGet()
        {
            IEnumerable<UserRole> userRoles = new List<UserRole>();
            userRoles = await RepoFactory.GetRepo().UserRoleGetAsync();
            return Ok(userRoles);
        }

        #region Group
        [HttpGet("Group/All")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<string>> UserGroupsGet()
        {
            try
            {
                IEnumerable<UserGroup> userGroups = new List<UserGroup>();
                userGroups = await RepoFactory.GetRepo().UserGroupsGetAsync();
                return Ok(userGroups);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        [HttpGet("Group")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<string>> UserGroupGet(int? idUserGroup)
        {
            try
            {
                UserGroup userGroup = await RepoFactory.GetRepo().UserGroupGetAsync(idUserGroup);
                return Ok(userGroup);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "User read operation failed");
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
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
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
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
                var kind = RepoFactory.GetRepo().ClassifyException(e);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, DbErrorResponse.For(kind));
            }
        }

        #endregion




    }
}
