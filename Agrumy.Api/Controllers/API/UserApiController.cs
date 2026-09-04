using System.Security.Cryptography;
using System.Text;
using api.Dal.Interface;
using api.Models;
using api.Notifications;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace api.Controllers.API
{
    [Route("api/User")]
    public class UserApiController(IRepository repo, ICache cache, INotificationDispatcher notifications, IOptions<AgrumySettings> settingsOptions)
        : ApiControllerBase(repo, cache)
    {
        private const int DefaultRoleId = 1;   // "regular user" group on an existing tenant
        private const int AccessTokenMinutes = 120;
        private const int RefreshTokenDays = 30;
        private const int ActivationTokenValidHours = 24;
        private const int MinTenantNameLength = 6;

        private readonly AgrumySettings settings = settingsOptions.Value;
        private string? SecureKey => settings.JwtSecureKey;

        /// <summary>New opaque token plus the hash that's actually stored - the plaintext never
        /// touches the DB. Shared by refresh tokens and activation tokens; same shape, same
        /// single-use-until-redeemed lifecycle.</summary>
        private static (string Plaintext, string Hash) GenerateOpaqueToken()
        {
            string plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            return (plaintext, HashToken(plaintext));
        }

        private static string HashToken(string plaintext) =>
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

            bool isNewTenant = !await Repo.TenantGetAsync(value.TenantName!);
            if (isNewTenant)
            {
                ServerConfig serverConfig = await Repo.ServerConfigGetAsync(1);
                if (!serverConfig.AllowSelfServiceTenantCreation)
                {
                    return StatusCode(403, "Unknown tenant name, and self-service tenant creation is disabled.");
                }
                if ((value.TenantName?.Length ?? 0) < MinTenantNameLength)
                {
                    return BadRequest($"Tenant name must be at least {MinTenantNameLength} characters.");
                }
            }

            var user = new User
            {
                Email = value.Email,
                Username = value.Username,
                FirstName = value.FirstName,
                LastName = value.LastName,
                Phone = value.Phone,
                EmailVerified = false, // proven only via GET /api/User/Activate below
            };

            var userSecret = new UserSecret { PwdSalt = AuthenticationProvider.GetSalt() };
            userSecret.PwdHash = AuthenticationProvider.GetHash(value.Password!, userSecret.PwdSalt); // [Required], guaranteed by ModelState.IsValid above

            if (isNewTenant)
            {
                // Brand-new tenant: the registrant becomes its admin - nobody else exists yet to approve them.
                user.TenantID = await Repo.TenantAddAsync(value.TenantName!);
                user.UserGroupID = 0;
            }
            else
            {
                user.TenantID = await Repo.TenantGetIdAsync(value.TenantName!);
                user.UserGroupID = DefaultRoleId;
            }

            // TenantID==0 (shared default tenant) has no owning admin to approve joiners, so it
            // auto-enables like a brand-new tenant's own creator does.
            user.Enabled = isNewTenant || user.TenantID == 0;

            await Repo.UserAddAsync(user, userSecret);

            // UserAddAsync doesn't return the new IDUser; re-fetch by the just-inserted unique email.
            User? added = await Repo.UserGetAsync(null, value.Email, null);
            if (added?.IDUser is int idUser)
            {
                var (plaintext, hash) = GenerateOpaqueToken();
                await Repo.UserSetActivationTokenAsync(idUser, hash, DateTime.UtcNow.AddHours(ActivationTokenValidHours));
                await SendActivationEmailAsync(user.Email, plaintext);

                // A new tenant's creator starts as its admin; everyone else starts as a read-only
                // Tenant reader until granted more via PUT /api/User/UserRoles.
                string startingRole = isNewTenant ? RoleNames.TenantAdmin : RoleNames.TenantReader;
                await Repo.UserRolesSetAsync(idUser, new[] { startingRole });
            }

            return Ok(user);
        }

        /// <summary>Proves the registrant owns the email address on file - the direct link a user
        /// clicks from their inbox, so it must work unauthenticated.</summary>
        [HttpGet("Activate")]
        [AllowAnonymous]
        public async Task<ActionResult> Activate([FromQuery] string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest("Missing activation token.");
            }

            User? user = await Repo.UserActivateAsync(HashToken(token));
            if (user is null)
            {
                return StatusCode(400, "Activation link is invalid or has expired.");
            }

            // TenantID==0 and a brand-new tenant's own creator were already Enabled at registration
            // above; anyone else joining an existing tenant still needs that tenant's admin to approve them.
            if (user.TenantID != 0 && user.Enabled != true)
            {
                await NotifyTenantAdminsOfPendingApprovalAsync(user);
                return Ok("Email verified. Your tenant administrator has been notified and must approve your account before you can sign in.");
            }
            return Ok("Email verified. You can now sign in.");
        }

        /// <summary>Re-sends the activation email. Rate-limited server-side by
        /// ServerConfig.ActivationResendCooldownMinutes (not just the IP-based "login" policy
        /// below) so a forgotten inbox can't be used to spam one address. Always returns the same
        /// generic message regardless of whether the account exists or is already verified.</summary>
        [HttpPost("ResendActivation")]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        public async Task<ActionResult> ResendActivation([FromBody] ResendActivationRequest value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            var (user, _) = await LookupAsync(value.Login);
            if (user?.IDUser is int idUser && user.EmailVerified != true)
            {
                int cooldownMinutes = (await Repo.ServerConfigGetAsync(1)).ActivationResendCooldownMinutes ?? settings.ActivationResendCooldownMinutes;
                var (plaintext, hash) = GenerateOpaqueToken();
                bool issued = await Repo.UserIssueActivationTokenAsync(idUser, hash, DateTime.UtcNow.AddHours(ActivationTokenValidHours), cooldownMinutes);
                if (issued)
                {
                    await SendActivationEmailAsync(user.Email, plaintext);
                }
            }
            return Ok("If that account exists and is not yet verified, a new activation email has been sent.");
        }

        private async Task SendActivationEmailAsync(string? email, string plaintextToken)
        {
            if (string.IsNullOrWhiteSpace(email)) { return; }

            string link = $"{settings.JwtIssuer}/api/User/Activate?token={Uri.EscapeDataString(plaintextToken)}";
            await notifications.DispatchAsync(new Notification(
                "Confirm your Agrumy account",
                $"Click the link below to verify your email address:\n{link}\n\nThis link expires in {ActivationTokenValidHours} hours.",
                new NotificationRecipient(Email: email)));
        }

        /// <summary>Tells every admin of the given tenant that a newly-verified user is waiting for
        /// approval. Never a no-op silently - a tenant can never have zero admins.</summary>
        private async Task NotifyTenantAdminsOfPendingApprovalAsync(User user)
        {
            IList<User> admins = await Repo.TenantAdminsGetAsync(user.TenantID!.Value);
            foreach (User admin in admins)
            {
                if (string.IsNullOrWhiteSpace(admin.Email)) { continue; }
                await notifications.DispatchAsync(new Notification(
                    "New user awaiting approval",
                    $"{user.Email} ({user.Username}) verified their email and is waiting for your approval before they can sign in.",
                    new NotificationRecipient(Email: admin.Email)));
            }
        }

        /// <summary>The full set of role-claim values this user's token should carry - their real
        /// role set from userUserRole, PLUS a prepended legacy "admin"/"user" alias so pre-existing
        /// [Authorize(Roles=...)]/CallerRole=="admin" checks that read only the FIRST role claim
        /// (see ApiControllerBase.CallerRole) still work. Falls back to the old UserGroupID-derived
        /// single role if userUserRole has nothing for this user yet.</summary>
        private async Task<IReadOnlyList<string>> ResolveCallerTokenRolesAsync(User user)
        {
            IReadOnlyList<string> roleNames = await Repo.UserRoleNamesGetAsync(user.IDUser!.Value);
            if (roleNames.Count == 0)
            {
                IList<UserRole> groupRoles = await Repo.UserRoleGetAsync();
                UserRole? legacyRole = groupRoles.FirstOrDefault(m => m.IDUserRole == user.UserRoleID);
                return legacyRole?.RoleName == null ? Array.Empty<string>() : new[] { legacyRole.RoleName };
            }

            string legacyAlias = RoleNames.ImpliesLegacyAdmin(roleNames) ? RoleNames.LegacyAdmin : RoleNames.LegacyUser;
            return new[] { legacyAlias }.Concat(roleNames).ToList();
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

            // Checked in this order so the more specific, actionable reason (verify your email)
            // surfaces before the generic "waiting for approval" one.
            if (user.EmailVerified != true)
            {
                return StatusCode(403, "Email address not verified yet - check your inbox for the activation link.");
            }
            if (user.Enabled != true)
            {
                return StatusCode(403, "Account not yet enabled - waiting for administrator approval.");
            }

            IReadOnlyList<string> tokenRoles = await ResolveCallerTokenRolesAsync(user);
            if (tokenRoles.Count == 0)
            {
                return StatusCode(500, "User has no valid role assigned.");
            }

            string token = JwtTokenProvider.CreateToken(SecureKey!, AccessTokenMinutes, user.Email!, tokenRoles, user.TenantID.ToString()!);
            var (refreshToken, refreshTokenHash) = GenerateOpaqueToken();
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

            string incomingHash = HashToken(value.RefreshToken!);
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
            // A refresh must not silently keep a since-disabled/unverified account logged in.
            if (user.EmailVerified != true || user.Enabled != true)
            {
                return StatusCode(403, "Account is not active.");
            }

            IReadOnlyList<string> tokenRoles = await ResolveCallerTokenRolesAsync(user);
            if (tokenRoles.Count == 0)
            {
                return StatusCode(500, "User has no valid role assigned.");
            }

            var (newRefreshToken, newRefreshTokenHash) = GenerateOpaqueToken();
            await Repo.RefreshTokenRotateAsync(incomingHash, newRefreshTokenHash, DateTime.UtcNow.AddDays(RefreshTokenDays));

            string newAccessToken = JwtTokenProvider.CreateToken(SecureKey!, AccessTokenMinutes, user.Email!, tokenRoles, user.TenantID.ToString()!);
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
                await Repo.RefreshTokenRevokeAsync(HashToken(value.RefreshToken));
            }
            return Ok();
        }

        /// <summary>Lets the anonymous Agrumy.Web login page decide, on every load, whether to show
        /// the normal login form or the first-run "set password" screen.</summary>
        [HttpGet("BootstrapPending")]
        [AllowAnonymous]
        public async Task<ActionResult<bool>> BootstrapPending() => Ok(await Repo.BootstrapAdminPendingAsync());

        /// <summary>The only way the fresh-install bootstrap Global Admin (seeded with
        /// PwdHash=NULL) gets a real password - see BootstrapAdminSetPasswordAsync for why this can
        /// never be replayed once it has succeeded. SetupSecret gates this beyond rate limiting
        /// alone (roadmap #179) - it's the token EfRepository.SeedBootstrapAdminAsync logged at
        /// first startup, so an anonymous visitor who finds this endpoint before the real admin
        /// reads that log can't take over the account.</summary>
        [HttpPost("BootstrapSetPassword")]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        public async Task<ActionResult> BootstrapSetPassword([FromBody] BootstrapAdminSetPassword value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            string salt = AuthenticationProvider.GetSalt();
            var secret = new UserSecret { PwdSalt = salt, PwdHash = AuthenticationProvider.GetHash(value.NewPassword!, salt) };

            return await Repo.BootstrapAdminSetPasswordAsync(secret, value.SetupSecret!)
                ? Ok()
                : StatusCode(403, "No pending bootstrap admin, or the setup secret was wrong.");
        }

        /// <summary>Identity comes ONLY from the JWT, not a Login field in the body - this used to
        /// take Login from the request with neither [Authorize] nor rate limiting, making it an
        /// unauthenticated, unthrottled oracle for guessing any known email's password via the
        /// 401-vs-403 response split.</summary>
        [HttpPost("ChangePassword")]
        [Authorize]
        [EnableRateLimiting("login")]
        public async Task<ActionResult<string>> UserSetPassword([FromBody] UserSetPassword value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            string? name = User.Identity?.Name;
            if (string.IsNullOrEmpty(name))
            {
                return Unauthorized();
            }

            if (value.OldPassword == value.NewPassword)
            {
                return StatusCode(403, "The new password must be different from the old password");
            }

            var (user, secret) = await LookupAsync(name);
            if (user is null || secret is null ||
                !AuthenticationProvider.VerifyHash(secret.PwdHash, secret.PwdSalt, value.OldPassword))
            {
                return StatusCode(401, "Wrong password");
            }

            secret.PwdSalt = AuthenticationProvider.GetSalt();
            secret.PwdHash = AuthenticationProvider.GetHash(value.NewPassword!, secret.PwdSalt); // [Required], guaranteed by ModelState.IsValid above

            return await Repo.UserSetPasswordAsync(user.Email, secret)
                ? Ok("Password changed successfully for: " + user.Email)
                : StatusCode(403, "Password change failed for: " + user.Email);
        }

        // ---- read ----------------------------------------------------------------

        /// <summary>Opened from admin-only to every authenticated caller - a Tenant reader's whole
        /// point is being able to SEE their tenant's resources without touching them.</summary>
        [HttpGet("All")]
        [Authorize]
        public async Task<ActionResult<IList<User>>> UsersGet() =>
            Ok(CallerReadsUsersGlobally ? await Repo.UsersGetAllAsync() : await Repo.UsersGetAsync(CallerTenantId));

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

        /// <summary>Self-scoped counterpart to the admin-only UserUpdate: identity comes ONLY from
        /// the JWT, and the payload has no authorization-bearing fields - Enabled/UserGroupID/
        /// TenantID stay untouchable by construction because Repo.UserProfileSetAsync writes
        /// nothing but FirstName/LastName/TimeZone.</summary>
        [HttpPut("Profile")]
        [Authorize]
        public async Task<ActionResult<bool>> UserProfileSet([FromBody] UserProfileUpdate value)
        {
            string? name = User.Identity?.Name;
            if (string.IsNullOrEmpty(name))
            {
                return Unauthorized();
            }

            string? timeZone = null;
            if (!string.IsNullOrWhiteSpace(value.TimeZone))
            {
                // Normalized to the IANA id before storing, so a Windows-id submission can never
                // persist a value the Linux server's ICU catalog would not resolve.
                if (!TimeZoneHelper.TryNormalizeToIana(value.TimeZone, out string iana))
                {
                    return BadRequest("Unknown time zone: " + value.TimeZone);
                }
                timeZone = iana;
            }

            return await Repo.UserProfileSetAsync(name, value.FirstName, value.LastName, timeZone)
                ? Ok(true)
                : NotFound();
        }

        /// <summary>(Re)issues the caller's device-registration PIN (multi-use within its 24h
        /// window, not consumed on first use). POST, not GET: every call rotates the PIN, which
        /// also serves as the only revocation mechanism.</summary>
        [HttpPost("DevicePin")]
        [Authorize]
        public async Task<ActionResult<DevicePinResult>> DevicePinGenerate()
        {
            string? name = User.Identity?.Name;
            if (string.IsNullOrEmpty(name))
            {
                return Unauthorized();
            }

            User? user = await Repo.UserGetAsync(null, name, null);
            if (user?.IDUser is not int idUser)
            {
                return NotFound();
            }

            string pin = AuthenticationProvider.GetPin();
            DateTime expiresAt = DateTime.UtcNow.AddHours(AuthenticationProvider.PinValidHours);
            await Repo.UserSetDevicePinAsync(idUser, pin, expiresAt);

            return Ok(new DevicePinResult { DevicePin = pin, ExpiresAt = expiresAt });
        }

        [HttpGet]
        [Authorize]
        public async Task<ActionResult<User>> UserGet(int idUser)
        {
            User? user = await Repo.UserGetAsync(idUser, null, null);
            if (user is null)
            {
                return NotFound();
            }
            return user.TenantID != CallerTenantId && !CallerReadsUsersGlobally
                ? StatusCode(403, "Target user belongs to a different tenant")
                : Ok(user);
        }

        // ---- write -------------------------------------------------------------

        [HttpPost]
        [Authorize(Roles = RoleNames.UserManagers)]
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
                EmailVerified = true, // an admin vouches for the address directly, bypassing the normal email-proof step
            };

            var userSecret = new UserSecret { PwdSalt = AuthenticationProvider.GetSalt() };
            userSecret.PwdHash = AuthenticationProvider.GetHash(value.Password!, userSecret.PwdSalt); // [Required], guaranteed by ModelState.IsValid above

            await Repo.UserAddAsync(user, userSecret);

            // Maps the legacy admin/user group choice onto a starting base role; an admin can layer
            // on more (or promote to Global via UserRolesSet) afterwards.
            User? added = await Repo.UserGetAsync(null, value.Email, null);
            if (added?.IDUser is int idUser)
            {
                string startingRole = value.UserGroupID == 0
                    ? (CallerIsGlobalAdmin ? RoleNames.GlobalAdmin : RoleNames.TenantAdmin)
                    : RoleNames.TenantReader;
                await Repo.UserRolesSetAsync(idUser, new[] { startingRole });
            }

            return Ok("User created successfully: " + user.Email);
        }

        [HttpPut]
        [Authorize(Roles = RoleNames.UserManagers)]
        public async Task<ActionResult<bool>> UserUpdate([FromBody] UserUpdate value)
        {
            if (!ModelState.IsValid) { return BadRequest(ModelState); }

            User? user = await Repo.UserGetAsync(value.IDUser, null, null);
            if (user is null)
            {
                return NotFound();
            }

            if (!CallerManagesUsers(user.TenantID))
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
            if (value.UserGroupID != null) { user.UserGroupID = value.UserGroupID; } // attribute already restricts to user-managers
            if (value.Enabled != null) { user.Enabled = value.Enabled; }
            if (value.TenantID != null && CallerManagesUsersGlobally) { user.TenantID = value.TenantID; } // cross-tenant reassignment stays a Global-admin-only power

            await Repo.UserUpdateAsync(user);
            return Ok(true);
        }

        [HttpDelete]
        [Authorize(Roles = RoleNames.UserManagers)]
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
            if (!CallerManagesUsers(targetUser.TenantID))
            {
                return StatusCode(403, "Target user belongs to a different tenant");
            }

            return await Repo.UserDeleteAsync(idUser) ? Ok("User deleted") : NotFound("User not found");
        }

        [HttpGet("Roles")]
        [Authorize(Roles = RoleNames.UserManagers)]
        public async Task<ActionResult<IEnumerable<UserRole>>> UserRoleGet() =>
            Ok(await Repo.UserRoleGetAsync());

        // ---- composable roles -------------------------------------

        /// <summary>Every role name a Tenant admin may grant - Global-* roles are a Global-admin-only power.</summary>
        private static readonly string[] TenantScopedGrantableRoles =
        {
            RoleNames.TenantAdmin, RoleNames.TenantReader, RoleNames.TenantUser, RoleNames.TenantDevice,
        };

        [HttpGet("UserRoles")]
        [Authorize(Roles = RoleNames.UserManagers)]
        public async Task<ActionResult<IReadOnlyList<string>>> UserRolesGet(int idUser)
        {
            User? target = await Repo.UserGetAsync(idUser, null, null);
            if (target is null)
            {
                return NotFound();
            }
            if (!CallerManagesUsers(target.TenantID))
            {
                return StatusCode(403, "Target user belongs to a different tenant");
            }
            return Ok(await Repo.UserRoleNamesGetAsync(idUser));
        }

        // Role GRANTING deliberately stays admin-only (RoleNames.Admins, not UserManagers): a
        // Tenant User could otherwise hand themselves Tenant admin - managing users must not
        // imply managing privileges.
        [HttpPut("UserRoles")]
        [Authorize(Roles = RoleNames.Admins)]
        public async Task<ActionResult> UserRolesSet([FromBody] UserRolesUpdate value)
        {
            User? target = await Repo.UserGetAsync(value.IDUser, null, null);
            if (target is null)
            {
                return NotFound();
            }
            if (target.TenantID != CallerTenantId && !CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Target user belongs to a different tenant");
            }

            HashSet<string> allowed = CallerIsGlobalAdmin ? RoleNames.All.ToHashSet() : TenantScopedGrantableRoles.ToHashSet();
            string? disallowed = value.RoleNames.FirstOrDefault(r => !allowed.Contains(r));
            if (disallowed != null)
            {
                return StatusCode(403, $"Not allowed to assign role \"{disallowed}\".");
            }

            await Repo.UserRolesSetAsync(value.IDUser, value.RoleNames);
            return Ok();
        }

        // ---- groups ----------------------------------------------------------

        // Group READS open to user-managers (the Web user Create/Edit forms need the dropdown);
        // group WRITES stay admin-only - groups still drive the legacy role mapping, so editing
        // them is privilege management, same reasoning as UserRolesSet above.

        [HttpGet("Group/All")]
        [Authorize(Roles = RoleNames.UserManagers)]
        public async Task<ActionResult<IEnumerable<UserGroup>>> UserGroupsGet() =>
            Ok(await Repo.UserGroupsGetAsync());

        [HttpGet("Group")]
        [Authorize(Roles = RoleNames.UserManagers)]
        public async Task<ActionResult<UserGroup>> UserGroupGet(int? idUserGroup)
        {
            UserGroup? group = await Repo.UserGroupGetAsync(idUserGroup);
            return group is null ? NotFound() : Ok(group);
        }

        [HttpPost("Group")]
        [Authorize(Roles = RoleNames.Admins)]
        public async Task<ActionResult<bool>> UserGroupAdd(UserGroup userGroup)
        {
            await Repo.UserGroupAddAsync(userGroup);
            return true;
        }

        [HttpDelete("Group")]
        [Authorize(Roles = RoleNames.Admins)]
        public async Task<ActionResult<bool>> UserGroupDelete(int? idUserGroup)
        {
            await Repo.UserGroupDeleteAsync(idUserGroup);
            return true;
        }
    }
}
