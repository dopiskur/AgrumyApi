using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace api.Controllers.API
{
    [Route("api/User")]
    [ApiController]
    public class UserApiController : ControllerBase
    {
        private static string? secureKey = Config.secureKey;

        // CONSTANTS
        private readonly int? DEFAULT_TENANTID = 0;
        private readonly int? DEFAULT_ROLEID = 1;
        private readonly bool? DEFAULT_USER_ENABLED = false;
        private readonly bool? TENANT_ENABLED = false;



        // User registration
        [HttpPost("Register")]
        public ActionResult<UserRegistration> UserRegistration([FromBody] UserRegistration value)
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
                if (TENANT_ENABLED == true && !RepoFactory.GetRepo().TenantGet(value.TenantName))
                {
                    user.TenantID = RepoFactory.GetRepo().TenantAdd(value.TenantName);
                    user.UserGroupID = 0; // set as admin on new tenant
                    user.Enabled = true; // set as enabled user
                }


                RepoFactory.GetRepo().UserAdd(user, userSecret);

                //return Ok("User created successfully: " + user.Email);
                return Ok(user);
            }
            catch (Exception e)
            {

                if (e.Message.Contains("email_UNIQUE"))
                {
                    return StatusCode(500, "email already registered");
                }

                if (e.Message.Contains("Username_UNIQUE"))
                {
                    return StatusCode(500, "username already registered");
                }

                //return StatusCode(500, "Unspecified error");
                return StatusCode(500, e.Message);


            }

        }

        // User login
        [HttpPost("Login")]
        public ActionResult<UserLogin> UserLogin([FromBody] UserLogin value)
        {
            //AuthProvider.VerifyPassword(value.Email,value.Password);

            try
            {
                if (!ModelState.IsValid) { return BadRequest(ModelState); }

                User user = new User();
                UserSecret userSecret = new UserSecret();

                if (FieldValidator.IsValidEmail(value.Login))
                {
                    user = RepoFactory.GetRepo().UserGet(null, value.Login, null);
                    userSecret = RepoFactory.GetRepo().UserSecretGet(null, value.Login, null);
                }
                else
                {
                    user = RepoFactory.GetRepo().UserGet(null, null, value.Login);
                    userSecret = RepoFactory.GetRepo().UserSecretGet(null, null, value.Login);
                }

                if (AuthenticationProvider.VerifyHash(userSecret.PwdHash, userSecret.PwdSalt, value.Password))
                {

                    IList<UserRole> roles = RepoFactory.GetRepo().UserRoleGet();
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

                return StatusCode(500, ex.Message);
            }
        }

        // Change password
        [HttpPost("ChangePassword")]
        public ActionResult<UserSetPassword> UserSetPassword([FromBody] UserSetPassword value)
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
                    user = RepoFactory.GetRepo().UserGet(null, value.Login, null);
                    userSecret = RepoFactory.GetRepo().UserSecretGet(null, value.Login, null);
                }
                else
                {
                    user = RepoFactory.GetRepo().UserGet(null, null, value.Login);
                    userSecret = RepoFactory.GetRepo().UserSecretGet(null, null, value.Login);
                }

                if (AuthenticationProvider.VerifyHash(userSecret.PwdHash, userSecret.PwdSalt, value.OldPassword))
                {
                    userSecret.PwdSalt = AuthenticationProvider.GetSalt();
                    userSecret.PwdHash = AuthenticationProvider.GetHash(value.NewPassword, userSecret.PwdSalt);

                    if (RepoFactory.GetRepo().UserSetPassword(user.Email, userSecret))
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
                return StatusCode(500, ex.Message);
            }
        }


        // Get all users, as admin
        [HttpGet("All")]
        //[Authorize(Roles = "admin")]
        [Authorize(Roles = "admin")]
        public ActionResult<IList<User>> UsersGet()
        {

            try
            {
                // var identity = HttpContext.User.Identity as ClaimsIdentity;
                // int tenantID = int.Parse(identity.FindFirst("TenantID").Value.ToString());

                IList<User> users = new List<User>();
                users = RepoFactory.GetRepo().UsersGet(DEFAULT_TENANTID);

                return Ok(users);

            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }

        }


        // get user by self
        [HttpGet("Self")]
        [Authorize(Roles = "admin")]
        //[Authorize(Roles = "admin, user")]
        public ActionResult<User> GetUserSelf()
        {
            try
            {
                var identity = HttpContext.User.Identity as ClaimsIdentity;
                User user;
                user = RepoFactory.GetRepo().UserGet(null, identity.Name, null);

                return Ok(user);
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }

        }


        // get user by ID
        [HttpGet]
        [Authorize(Roles = "admin")]
        public ActionResult<User> UserGet(int idUser)
        {
            try
            {
                
                var identity = HttpContext.User.Identity as ClaimsIdentity;

                if (!(identity.FindFirst(ClaimTypes.Role).Value == "admin"))
                {
                    //user = RepositoryFactory.GetRepository().UserGet(null, identity.Name, null);
                    return Unauthorized();
                }
                

                User user = RepoFactory.GetRepo().UserGet(idUser, null, null);

                return Ok(user);
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }

        }


        // Create user as admin
        [HttpPost]
        [Authorize(Roles = "admin")]
        public ActionResult<UserAdd> UserAdd([FromBody] UserAdd? value)
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


                RepoFactory.GetRepo().UserAdd(user, userSecret);

                return Ok("User created successfully: " + user.Email);
            }
            catch (Exception e)
            {

                if (e.Message.Contains("email_UNIQUE"))
                {
                    return StatusCode(500, "email already registered");
                }

                if (e.Message.Contains("Username_UNIQUE"))
                {
                    return StatusCode(500, "username already registered");
                }

                //return StatusCode(500, "Unspecified error");
                return StatusCode(500, e.Message);


            }

        }

        // Update users ad admin, or self as user 
        [HttpPut]
        [Authorize(Roles = "admin")]
        public ActionResult<bool> UserUpdate([FromBody] UserUpdate value)
        {
            try
            {
                bool result = false;
                if (!ModelState.IsValid) { return BadRequest(ModelState); }
                var identity = HttpContext.User.Identity as ClaimsIdentity;

                User user = RepoFactory.GetRepo().UserGet(value.IDUser, null, null);

                if (identity.FindFirst(ClaimTypes.Role).Value == "user" && value.Email != identity.Name) // if user, jwt token name must be equal to email identity
                {
                    return Unauthorized();
                }


                // check for password change
                UserSecret userSecret = RepoFactory.GetRepo().UserSecretGet(value.IDUser, null, null);
                if (value.Password != null)
                {

                    userSecret.PwdSalt = AuthenticationProvider.GetSalt();
                    userSecret.PwdHash = AuthenticationProvider.GetHash(value.Password, userSecret.PwdSalt);
                    RepoFactory.GetRepo().UserSetPassword(user.Email, userSecret);
                }

                // if (value.TenantID != null) { user.TenantID = value.TenantID; } // ovo ostavljamo za iducu iteraciju
                if (value.Email != null) { user.Email = value.Email; }
                if (value.Username != null) { user.Username = value.Username; }
                if (value.FirstName != null) { user.FirstName = value.FirstName; }
                if (value.LastName != null) { user.LastName = value.LastName; }
                if (value.Phone != null) { user.Phone = value.Phone; }
                if (value.UserGroupID != null && identity.FindFirst(ClaimTypes.Role).Value == "admin") { user.UserGroupID = value.UserGroupID; } // can change roleid only if admin
                if (value.Enabled != null && identity.FindFirst(ClaimTypes.Role).Value == "admin") { user.Enabled = value.Enabled; } // can change enabled status only if admin


                RepoFactory.GetRepo().UserUpdate(user);

                return Ok(result=true);


            }
            catch (Exception e)
            {

                if (e.Message.Contains("email_UNIQUE"))
                {
                    return StatusCode(500, "email already registered");
                }

                if (e.Message.Contains("Username_UNIQUE"))
                {
                    return StatusCode(500, "username already registered");
                }

                //return StatusCode(500, "Unspecified error");
                return StatusCode(500, e.Message);


            }

        }

        // DELETE api/<UserController>/5
        [HttpDelete]
        [Authorize(Roles = "admin")]
        public ActionResult<string> Delete(int? idUser)
        {
            try
            {

                
                if (idUser > 1) // preventing deletion of admin
                {
                    if (RepoFactory.GetRepo().UserDelete(idUser))
                    {
                        return Ok("User deleted");
                    };
                    return NotFound("User not found");
                }
                

                return Unauthorized("Deleting default user is not allowed");
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }

        }


        // User Role List
        [HttpGet("Roles")]
        [Authorize(Roles = "admin")]
        public ActionResult<string> UserRoleGet()
        {
            IEnumerable<UserRole> userRoles = new List<UserRole>();
            userRoles = RepoFactory.GetRepo().UserRoleGet();
            return Ok(userRoles);
        }

        #region Group
        // User Role List
        [HttpGet("Group/All")]
        [Authorize(Roles = "admin")]
        public ActionResult<string> UserGroupsGet()
        {
            try
            {
                IEnumerable<UserGroup> userRoles = new List<UserGroup>();
                userRoles = RepoFactory.GetRepo().UserGroupsGet();
                return Ok(userRoles);
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }

        [HttpGet("Group")]
        [Authorize(Roles = "admin")]
        public ActionResult<string> UserGroupGet(int? idUserGroup)
        {
            try
            {
                UserGroup userGroup = RepoFactory.GetRepo().UserGroupGet(idUserGroup);
                return Ok(userGroup);
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }



        [HttpPost("Group")]
        [Authorize(Roles = "admin")]
        public ActionResult<bool> UserGroupAdd(UserGroup userGroup)
        {

            try
            {
                RepoFactory.GetRepo().UserGroupAdd(userGroup);
                return true;
            }
            catch (Exception e)
            {

                return false;
            }
        }

        [HttpDelete("Group")]
        [Authorize(Roles = "admin")]
        public ActionResult<bool> UserGroupDelete(int? idUserGroup)
        {
            try
            {
            RepoFactory.GetRepo().UserGroupDelete(idUserGroup);
            return true;
            }
            catch (Exception e)
            {

                return false;
            }
        }

        #endregion




    }
}
