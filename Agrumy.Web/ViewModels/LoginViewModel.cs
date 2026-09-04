using System.ComponentModel.DataAnnotations;

namespace api.ViewModels
{
    public class SetupAdminViewModel
    {
        // From the server console/log at first startup (roadmap #179) - see EfRepository.SeedBootstrapAdminAsync.
        [Required(ErrorMessage = "Setup secret is required")]
        [Display(Name = "Setup Secret")]
        public string? SetupSecret { get; set; }

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
