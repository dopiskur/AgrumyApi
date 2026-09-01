using api.Dal.Interface;
using api.Models;
using api.Utils;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.Controllers.View
{
    /// <summary>Self-service "My Profile" (roadmap #71 follow-up) - open to EVERY authenticated
    /// user, unlike UserController's user-manager gate: this page only ever edits the caller's own
    /// record through the self-scoped API endpoints (Self / Profile / ChangePassword).</summary>
    [Authorize]
    public class ProfileController(IApi api) : Controller
    {
        public async Task<ActionResult> Index()
        {
            return View(await BuildViewModelAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Index(ProfileViewModel value)
        {
            if (!ModelState.IsValid)
            {
                return View(await RestoreDisplayFieldsAsync(value));
            }

            try
            {
                await api.UserProfileSet(value.Profile);
            }
            catch (ApiException ex)
            {
                // Field-keyed (not string.Empty) so the error renders once, in this form - the page
                // holds a second form (_ChangePassword) whose summary would otherwise repeat it.
                ModelState.AddModelError("Profile.TimeZone", ex.Body);
                return View(await RestoreDisplayFieldsAsync(value));
            }

            TempData["ProfileMessage"] = "Profile saved.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Roadmap #70: rotates the caller's device-registration PIN via the self-scoped
        /// API endpoint; the redirect re-renders the page with the fresh PIN and its expiry.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DevicePin()
        {
            await api.DevicePinGenerate();
            TempData["ProfileMessage"] = "New device PIN generated - it is valid for 24 hours and can be used to register as many devices as you need in that window.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ChangePassword(ChangePasswordViewModel value)
        {
            User self = await api.UserGetSelf();
            if (ModelState.IsValid)
            {
                try
                {
                    // Reuses the existing old-password-proving API flow (POST /api/User/ChangePassword);
                    // identity comes from the caller's own JWT server-side (roadmap #83), never from the form.
                    await api.ChangePassword(new UserSetPassword
                    {
                        OldPassword = value.OldPassword,
                        NewPassword = value.NewPassword,
                    });
                    TempData["ProfileMessage"] = "Password changed.";
                    return RedirectToAction(nameof(Index));
                }
                catch (ApiException ex)
                {
                    // Keyed to OldPassword for the same single-render reason as the profile form.
                    ModelState.AddModelError(nameof(ChangePasswordViewModel.OldPassword), ex.Body);
                }
            }

            // Re-render the combined page with the password errors visible and profile prefilled.
            return View(nameof(Index), BuildViewModel(self));
        }

        private async Task<ProfileViewModel> BuildViewModelAsync() => BuildViewModel(await api.UserGetSelf());

        private static ProfileViewModel BuildViewModel(User self) => new()
        {
            Email = self.Email,
            Profile = new UserProfileUpdate
            {
                FirstName = self.FirstName,
                LastName = self.LastName,
                TimeZone = self.TimeZone,
            },
            TimeZones = TimeZoneOptions(self.TimeZone),
            DevicePin = self.DevicePin,
            DevicePinExpires = self.DevicePinExpires,
        };

        /// <summary>The PIN fields are display-only (never posted back), so an error re-render must
        /// refetch them or the card would go blank while the user fixes a validation message.</summary>
        private async Task<ProfileViewModel> RestoreDisplayFieldsAsync(ProfileViewModel value)
        {
            User self = await api.UserGetSelf();
            value.Email = self.Email;
            value.DevicePin = self.DevicePin;
            value.DevicePinExpires = self.DevicePinExpires;
            value.TimeZones = TimeZoneOptions(value.Profile.TimeZone);
            return value;
        }

        private static List<SelectListItem> TimeZoneOptions(string? selected) =>
            TimeZoneHelper.GetTimeZoneOptions()
                .Select(o => new SelectListItem(o.DisplayName, o.Id, string.Equals(o.Id, selected, StringComparison.OrdinalIgnoreCase)))
                .ToList();
    }
}
