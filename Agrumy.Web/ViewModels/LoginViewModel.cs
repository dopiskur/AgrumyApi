using System.ComponentModel.DataAnnotations;

namespace api.ViewModels
{
    /// <summary>Roadmap #91: the first-run "set password" screen Agrumy.Web shows instead of the
    /// normal login form while the bootstrap Global Admin still has PwdHash=NULL. Separate from
    /// api.Models.BootstrapAdminSetPassword for the same reason ChangePasswordViewModel is separate
    /// from UserSetPassword - the API model carries no confirm-password field, that's a
    /// client-side-only concern.</summary>
    public class SetupAdminViewModel
    {
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
