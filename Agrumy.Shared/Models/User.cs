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

        // Roadmap #70: a PIN is only registerable while unexpired; null (legacy rows, or a PIN
        // consumed by a successful device registration) means "generate a new one first".
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

        // Roadmap #24: has this user proven they own their email address yet.
        public bool? EmailVerified { get; set; }

        // IANA time zone id (e.g. "Europe/Zagreb") for display conversion of stored-UTC
        // timestamps; null = show UTC (see api.Utils.TimeZoneHelper).
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
        
        [HiddenInput(DisplayValue = true)] // sakrivamo atribut od editiranja
        public int? IDUser { get; set; }
        [HiddenInput(DisplayValue = true)]
        public int? TenantID { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        [Phone(ErrorMessage = "Provide a correct phone number")]
        public string? Phone { get; set; }
        [Display(Name = "Role")]
        public int? UserGroupID { get; set; }

        // Usputni nalaz: was a non-nullable bool defaulting to false, so ANY admin PUT that didn't
        // explicitly re-send "Enabled": true silently disabled the target user (UserApiController's
        // "value.Enabled != null" check is always true for a non-nullable bool). Nullable now, same
        // "null = don't touch" convention as UserGroupID above - the Web Edit form already always
        // posts True/False explicitly (_EnabledToggleField.cshtml), so this only changes behaviour
        // for direct API callers that omit the field.
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
        public string? Password { get; set; }

        public string? Message { get; set; }
    }


    public class UserLoginResult
    {
        public int? IDUser { get; set; }
        public string? Email { get; set; }
        /// <summary>Long-lived opaque token that redeems a new <see cref="Token"/> from
        /// <c>POST /api/User/RefreshToken</c> once this JWT expires. Single-use - rotated on
        /// every redemption.</summary>
        public string? RefreshToken { get; set; }
        public string? Token { get; set; }
    }

    public class UserSetPassword
    {
        [Required(ErrorMessage = "Email or username is required")]
        public string? Login { get; set; }
        [Required(ErrorMessage = "Old password is required")]
        public string? OldPassword { get; set; }
        [Required(ErrorMessage = "New password is required")]
        public string? NewPassword { get; set; }
    }

    /// <summary>Body of PUT /api/User/Profile - the ONLY fields a user may change on their own
    /// account (identity comes from the JWT, never from here - roadmap #47 pattern). Deliberately
    /// has no Enabled/UserGroupID/TenantID so self-service can never touch authorization; the
    /// password goes through the separate ChangePassword flow that proves the old password.</summary>
    public class UserProfileUpdate
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        [Display(Name = "Time Zone")]
        public string? TimeZone { get; set; }
    }

    /// <summary>Roadmap #70: response of POST /api/User/DevicePin - the freshly generated PIN and
    /// when it stops being accepted by POST /api/Device/Register. The PIN is consumed by the first
    /// successful registration, so registering another device always starts here.</summary>
    public class DevicePinResult
    {
        public string? DevicePin { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    /// <summary>Roadmap #24. Deliberately the same shape as <see cref="UserLogin"/>'s Login field
    /// (email or username) - a user who forgot which one they registered with shouldn't have to guess.</summary>
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

    /// <summary>Roadmap #66: replaces a user's ENTIRE composable role set (not incremental) - see
    /// api.Security.RoleNames for the valid values.</summary>
    public class UserRolesUpdate
    {
        public int IDUser { get; set; }
        public List<string> RoleNames { get; set; } = new();
    }



}
