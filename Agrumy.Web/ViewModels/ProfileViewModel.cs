using System.ComponentModel.DataAnnotations;
using api.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.ViewModels
{
    /// <summary>Self-service "My Profile" page (roadmap #71 follow-up): only the fields the profile
    /// endpoint accepts, plus the time-zone dropdown source - deliberately NOT UserView/UserUpdate,
    /// which carry admin-only fields (Enabled/UserGroupID/TenantID) this page must never post.</summary>
    public class ProfileViewModel
    {
        public string? Email { get; set; }
        public UserProfileUpdate Profile { get; set; } = new();
        public IEnumerable<SelectListItem> TimeZones { get; set; } = [];
    }

    /// <summary>Separate from UserSetPassword because the API model requires a Login field the page
    /// must not bind from the form - the controller injects the caller's own identity server-side.</summary>
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Old password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Old Password")]
        public string? OldPassword { get; set; }

        [Required(ErrorMessage = "New password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        public string? NewPassword { get; set; }

        [Required(ErrorMessage = "Password confirmation is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm New Password")]
        [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match")]
        public string? ConfirmPassword { get; set; }
    }
}
