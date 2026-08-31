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
            User self = await api.UserGetSelf();
            return View(new ProfileViewModel
            {
                Email = self.Email,
                Profile = new UserProfileUpdate
                {
                    FirstName = self.FirstName,
                    LastName = self.LastName,
                    TimeZone = self.TimeZone,
                },
                TimeZones = TimeZoneOptions(self.TimeZone),
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Index(ProfileViewModel value)
        {
            if (!ModelState.IsValid)
            {
                value.TimeZones = TimeZoneOptions(value.Profile.TimeZone);
                return View(value);
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
                value.TimeZones = TimeZoneOptions(value.Profile.TimeZone);
                return View(value);
            }

            TempData["ProfileMessage"] = "Profile saved.";
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
                    // Login comes from the caller's own identity, never from the form.
                    await api.ChangePassword(new UserSetPassword
                    {
                        Login = self.Email,
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
            return View(nameof(Index), new ProfileViewModel
            {
                Email = self.Email,
                Profile = new UserProfileUpdate
                {
                    FirstName = self.FirstName,
                    LastName = self.LastName,
                    TimeZone = self.TimeZone,
                },
                TimeZones = TimeZoneOptions(self.TimeZone),
            });
        }

        private static List<SelectListItem> TimeZoneOptions(string? selected) =>
            TimeZoneHelper.GetTimeZoneOptions()
                .Select(o => new SelectListItem(o.DisplayName, o.Id, string.Equals(o.Id, selected, StringComparison.OrdinalIgnoreCase)))
                .ToList();
    }
}
