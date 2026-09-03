using System.Security.Claims;
using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using api.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [AllowAnonymous]
    public class LoginController(IApi api, IAuthApi authApi, ILogger<LoginController> logger) : Controller
    {
        public async Task<ActionResult> Index(bool sessionExpired = false)
        {
            if (await BootstrapPendingSafeAsync())
            {
                return RedirectToAction(nameof(SetupAdmin));
            }

            if (sessionExpired)
            {
                ModelState.AddModelError(string.Empty, "Your session expired - please sign in again.");
            }
            return View(new UserLogin());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Index(UserLogin userLogin)
        {
            UserLoginResult? result;
            IReadOnlyList<string>? roles;
            try
            {
                result = await api.UserLogin(userLogin);
                roles = result?.Token is { } token ? JwtTokenProvider.ValidateToken(token) : null;
            }
            catch (ApiException ex)
            {
                // Wrong credentials also land here (the API answers 4xx) - expected, so only a warning.
                logger.LogWarning("Login rejected by Agrumy.Api ({StatusCode}).", ex.StatusCode);
                result = null;
                roles = null;
            }
            catch (Exception ex)
            {
                // Distinguish from a wrong-password rejection in the log; the user sees the same generic message either way.
                logger.LogError(ex, "Login call to Agrumy.Api failed.");
                result = null;
                roles = null;
            }

            if (result?.Token is null || result.RefreshToken is null || roles is null || roles.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Invalid email/username or password.");
                return View(userLogin);
            }

            // HttpOnly, SameSite=Strict cookie; the raw JWT/refresh token are stored tokens for BearerTokenHandler, never exposed to page script.
            var claims = new List<Claim> { new(ClaimTypes.Name, result.Email ?? "") };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            var props = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
            };
            props.StoreTokens(new[]
            {
                new AuthenticationToken { Name = "access_token", Value = result.Token },
                new AuthenticationToken { Name = "refresh_token", Value = result.RefreshToken },
            });

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), props);

            return RedirectToAction("Index", "DeviceUnit");
        }

        public async Task<ActionResult> Logout()
        {
            // Best effort: a dead/unreachable API must never block the local sign-out below.
            string? refreshToken = await HttpContext.GetTokenAsync("refresh_token");
            if (!string.IsNullOrEmpty(refreshToken))
            {
                try
                {
                    await authApi.RevokeRefreshToken(new RefreshTokenRequest { RefreshToken = refreshToken });
                }
                catch (Exception)
                {
                }
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Login");
        }

        // Re-checks BootstrapPending on every load (GET and POST) so this can never become a standing unauthenticated password-reset route.
        public async Task<ActionResult> SetupAdmin()
        {
            if (!await BootstrapPendingSafeAsync())
            {
                return RedirectToAction(nameof(Index));
            }
            return View(new SetupAdminViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SetupAdmin(SetupAdminViewModel value)
        {
            if (!await BootstrapPendingSafeAsync())
            {
                return RedirectToAction(nameof(Index));
            }
            if (!ModelState.IsValid)
            {
                return View(value);
            }

            try
            {
                await api.BootstrapSetPassword(new BootstrapAdminSetPassword { NewPassword = value.NewPassword });
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Body);
                return View(value);
            }

            TempData["Message"] = "Admin password set - you can now sign in.";
            return RedirectToAction(nameof(Index));
        }

        // Fails closed to the normal login form on any error.
        private async Task<bool> BootstrapPendingSafeAsync()
        {
            try
            {
                return await api.BootstrapPending();
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<ActionResult> Register()
        {
            await SetTenantCreationViewBagAsync();
            return View(new UserRegistration());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(UserRegistration value)
        {
            if (!ModelState.IsValid)
            {
                await SetTenantCreationViewBagAsync();
                return View(value);
            }

            try
            {
                await api.UserRegister(value);
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Body);
                await SetTenantCreationViewBagAsync();
                return View(value);
            }

            TempData["Message"] = "Account created. Check your email for a verification link - " +
                "depending on your tenant, you may also need an administrator's approval before you can sign in.";
            return RedirectToAction(nameof(Index));
        }

        private async Task SetTenantCreationViewBagAsync()
        {
            bool allow = false;
            try
            {
                allow = (await api.ServerConfigGetPublic()).AllowSelfServiceTenantCreation;
            }
            catch (Exception)
            {
                // ignored - ViewBag stays false, the safer default
            }
            ViewBag.AllowSelfServiceTenantCreation = allow;
        }
    }
}
