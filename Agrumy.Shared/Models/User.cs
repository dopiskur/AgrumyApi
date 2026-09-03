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

        public int? UserGroupID { get; set; }
        [Display(Name = "Role")]
        public int? UserRoleID { get; set; }
        public string? GroupName { get; set; }
        public bool? Enabled { get; set; } // MySQL TINYINT(1) is needed for boolean
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }

        public bool? EmailVerified { get; set; }

        // IANA time zone id (e.g. "Europe/Zagreb") for display conversion of stored-UTC timestamps; null = show UTC.
        public string? TimeZone { get; set; }
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
        [Display(Name = "Role")]
        public int? UserGroupID { get; set; } = 0;
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
        [Display(Name = "Role")]
        public int? UserGroupID { get; set; }

        // null = don't touch (same convention as UserGroupID above).
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
        /// <summary>Long-lived opaque token that redeems a new <see cref="Token"/> once this JWT expires. Single-use - rotated on every redemption.</summary>
        public string? RefreshToken { get; set; }
        public string? Token { get; set; }
    }

    /// <summary>Body of POST /api/User/ChangePassword - no Login field on purpose, identity comes only from the caller's JWT, so this can never be used as an unauthenticated password-guessing oracle.</summary>
    public class UserSetPassword
    {
        [Required(ErrorMessage = "Old password is required")]
        public string? OldPassword { get; set; }
        [Required(ErrorMessage = "New password is required")]
        public string? NewPassword { get; set; }
    }

    /// <summary>Body of POST /api/User/BootstrapSetPassword - no Login/email field: at most one pending bootstrap admin row (PwdHash IS NULL) can ever apply, so identifying by email would only add an unauthenticated probing surface.</summary>
    public class BootstrapAdminSetPassword
    {
        [Required(ErrorMessage = "New password is required")]
        public string? NewPassword { get; set; }
    }

    /// <summary>Body of PUT /api/User/Profile - the only fields a user may change on their own account (identity comes from the JWT, never from here). Deliberately has no Enabled/UserGroupID/TenantID so self-service can never touch authorization.</summary>
    public class UserProfileUpdate
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        [Display(Name = "Time Zone")]
        public string? TimeZone { get; set; }
    }

    /// <summary>Response of POST /api/User/DevicePin - the freshly generated PIN and when it stops being accepted. Valid for repeated registrations until that expiry (not consumed by the first one), so bulk sensor setup needs only one PIN.</summary>
    public class DevicePinResult
    {
        public string? DevicePin { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    /// <summary>Deliberately the same shape as <see cref="UserLogin"/>'s Login field (email or username) - a user who forgot which one they registered with shouldn't have to guess.</summary>
    public class ResendActivationRequest
    {
        [Required(ErrorMessage = "Email or username is required")]
        public string? Login { get; set; }
    }


    public class UserGroup
    {
        [Display(Name = "User Group")]
        public int? IDUserGroup { get; set; }
        public string? GroupName { get; set; }
        public int? UserRoleID { get; set; }
        public string? RoleName { get; set; }

    }




    public class UserRole
    {
        [Display(Name = "User Role")]
        public int? IDUserRole { get; set; }
        public string? RoleName { get; set; }
        public int? RoleScopeID { get; set; }
    }

    /// <summary>Replaces a user's entire composable role set (not incremental) - see api.Security.RoleNames for the valid values.</summary>
    public class UserRolesUpdate
    {
        public int IDUser { get; set; }
        public List<string> RoleNames { get; set; } = new();
    }



}
