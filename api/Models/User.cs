using api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.VisualStudio.Web.CodeGeneration.CommandLine;
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
        public int? DevicePin { get; set; } = AuthenticationProvider.GetPin();
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
        public int? DevicePin { get; set; } = AuthenticationProvider.GetPin();
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
        public int? DevicePin { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        [Phone(ErrorMessage = "Provide a correct phone number")]
        public string? Phone { get; set; }
        [Display(Name = "Role")]
        public int? UserGroupID { get; set; }
        public bool Enabled { get; set; } = false;
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



}
