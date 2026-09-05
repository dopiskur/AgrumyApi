using api.Security;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace api.Models
{
    public class User
    {
        public int? IDUser { get; set; }
        public int? TenantID { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? DevicePin { get; set; } = AuthenticationProvider.GetPin();

        // Multi-use within the validity window - not consumed by a successful device registration; null means generate a new one first.
        public DateTime? DevicePinExpires { get; set; } = DateTime.UtcNow.AddHours(AuthenticationProvider.PinValidHours);
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }

        public bool? Enabled { get; set; } // MySQL TINYINT(1) is needed for boolean
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }

        public bool? EmailVerified { get; set; }

        // IANA time zone id (e.g. "Europe/Zagreb") for display conversion of stored-UTC timestamps; null = show UTC.
        public string? TimeZone { get; set; }

        // Set only by tenant import - the imported hash is portable but unproven on this server, so login is blocked (UserApiController.UserLogin's 428 gate) until ForceChangePassword clears it.
        public bool MustChangePassword { get; set; }

        // Written only by EfRepository.RevokeUserTokensAsync (password change, Enabled->false) - an access token whose iat predates this is rejected even though it hasn't naturally expired yet.
        public DateTime? TokensValidAfterUtc { get; set; }
    }

    public class UserSecret
    {
        public string? PwdHash { get; set; }
        public string? PwdSalt { get; set; }

    }


    public class UserAdd
    {
        public int? TenantID { get; set; } = 0;
        public string? Email { get; set; }
        public string? Username { get; set; }
        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string? Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        // Non-admin callers can't grant roles - UserApiController.UserAdd falls back to Tenant reader regardless of what's sent here.
        public List<string> RoleNames { get; set; } = new();
        [DefaultValue(true)]
        public bool Enabled { get; set; } = true;
    }


    public class UserUpdate
    {
        [HiddenInput(DisplayValue = true)]
        public int? IDUser { get; set; }
        [HiddenInput(DisplayValue = true)]
        public int? TenantID { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        [DataType(DataType.Password)]
        public string? Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        [Phone(ErrorMessage = "Provide a correct phone number")]
        public string? Phone { get; set; }

        // null = don't touch (same convention as Enabled below); non-admin callers can't change roles.
        public List<string>? RoleNames { get; set; }

        // null = don't touch.
        public bool? Enabled { get; set; }
    }
    public class UserRegistration
    {

        public string? TenantName { get; set; } = "default";

        [Required(ErrorMessage = "Email is required")]
        public string? Email { get; set; }
        [Required(ErrorMessage = "Username is required")]
        public string? Username { get; set; }
        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string? Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        [Phone(ErrorMessage = "Provide a correct phone number")]
        public string? Phone { get; set; } = null;

    }

    public class UserLogin
    {
        [Required(ErrorMessage = "Email or username is required")]
        public string? Login { get; set; }
        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string? Password { get; set; }
    }


    public class UserLoginResult
    {
        public int? IDUser { get; set; }
        public string? Email { get; set; }
        /// Long-lived opaque token that redeems a new <see cref="Token"/> once this JWT expires. Single-use - rotated on every redemption.
        public string? RefreshToken { get; set; }
        public string? Token { get; set; }
    }

    /// Body of POST /api/User/ChangePassword - no Login field on purpose, identity comes only from the caller's JWT, so this can never be used as an unauthenticated password-guessing oracle.
    public class UserSetPassword
    {
        [Required(ErrorMessage = "Old password is required")]
        public string? OldPassword { get; set; }
        [Required(ErrorMessage = "New password is required")]
        public string? NewPassword { get; set; }
    }

    /// Body of POST /api/User/ForceChangePassword - same shape as UserSetPassword plus Login, since this is reachable before a normal JWT exists (see User.MustChangePassword).
    public class UserForceChangePassword
    {
        [Required(ErrorMessage = "Email or username is required")]
        public string? Login { get; set; }
        [Required(ErrorMessage = "Old password is required")]
        public string? OldPassword { get; set; }
        [Required(ErrorMessage = "New password is required")]
        public string? NewPassword { get; set; }
    }

    /// Body of POST /api/User/BootstrapSetPassword - SetupSecret (logged server-side at first startup) is required so this anonymous endpoint isn't just a rate-limit-only race to claim the Global Admin account.
    public class BootstrapAdminSetPassword
    {
        [Required(ErrorMessage = "New password is required")]
        public string? NewPassword { get; set; }
        [Required(ErrorMessage = "Setup secret is required")]
        public string? SetupSecret { get; set; }
    }

    /// Body of PUT /api/User/Profile - the only fields a user may change on their own account (identity comes from the JWT, never from here). Deliberately has no Enabled/TenantID so self-service can never touch authorization.
    public class UserProfileUpdate
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        [Display(Name = "Time Zone")]
        public string? TimeZone { get; set; }
    }

    /// Response of POST /api/User/DevicePin - the freshly generated PIN and when it stops being accepted. Valid for repeated registrations until that expiry (not consumed by the first one), so bulk sensor setup needs only one PIN.
    public class DevicePinResult
    {
        public string? DevicePin { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    /// Deliberately the same shape as <see cref="UserLogin"/>'s Login field (email or username) - a user who forgot which one they registered with shouldn't have to guess.
    public class ResendActivationRequest
    {
        [Required(ErrorMessage = "Email or username is required")]
        public string? Login { get; set; }
    }





    public class UserRole
    {
        [Display(Name = "User Role")]
        public int? IDUserRole { get; set; }
        public string? RoleName { get; set; }
        public int? RoleScopeID { get; set; }
    }

    /// Replaces a user's entire composable role set (not incremental) - see api.Security.RoleNames for the valid values.
    public class UserRolesUpdate
    {
        public int IDUser { get; set; }
        public List<string> RoleNames { get; set; } = new();
    }



}
