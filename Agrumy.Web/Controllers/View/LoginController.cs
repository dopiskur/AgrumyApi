using System.Security.Claims;
using System.Text.Json;
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
        private static readonly JsonSerializerOptions ImportJsonOptions = new(JsonSerializerDefaults.Web);

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
                // 428 = MustChangePassword (tenant import) - a distinct status
                // specifically so this branches without parsing message text, unlike the
                // wrong-credentials/not-verified/not-enabled cases below, which all still just
                // show the same generic error.
                if (ex.StatusCode == 428)
                {
                    TempData["ForceChangePasswordLogin"] = userLogin.Login;
                    return RedirectToAction(nameof(ForceChangePassword));
                }
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

            await SignInAsync(result, roles);
            return RedirectToAction("Index", "DeviceUnit");
        }

        /// <summary>Tenant-import counterpart to the login form - reached only via the 428 redirect
        /// above (see api.Models.User.MustChangePassword). GET pre-fills Login from TempData when
        /// the redirect carried it; a direct visit still works, just with an empty field.</summary>
        public ActionResult ForceChangePassword()
        {
            return View(new UserForceChangePassword { Login = TempData["ForceChangePasswordLogin"] as string });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ForceChangePassword(UserForceChangePassword value)
        {
            if (!ModelState.IsValid)
            {
                return View(value);
            }

            UserLoginResult? result;
            IReadOnlyList<string>? roles;
            try
            {
                result = await api.UserForceChangePassword(value);
                roles = result?.Token is { } token ? JwtTokenProvider.ValidateToken(token) : null;
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Body);
                return View(value);
            }

            if (result?.Token is null || result.RefreshToken is null || roles is null || roles.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Could not sign in after the password change.");
                return View(value);
            }

            await SignInAsync(result, roles);
            return RedirectToAction("Index", "DeviceUnit");
        }

        /// <summary>Shared by Index(POST) and ForceChangePassword(POST) - both end with the exact
        /// same cookie sign-in once Agrumy.Api hands back a token.</summary>
        private async Task SignInAsync(UserLoginResult result, IReadOnlyList<string> roles)
        {
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
                new AuthenticationToken { Name = "access_token", Value = result.Token! }, // caller already checked both for null before this ever runs
                new AuthenticationToken { Name = "refresh_token", Value = result.RefreshToken! },
            });

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), props);
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
                await api.BootstrapSetPassword(new BootstrapAdminSetPassword { NewPassword = value.NewPassword, SetupSecret = value.SetupSecret });
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Body);
                return View(value);
            }

            TempData["Message"] = "Admin password set - you can now sign in.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Imports a TenantExport as TenantID=0 (this server's sole
        /// tenant), replacing the unclaimed bootstrap admin. Same "re-check BootstrapPending on
        /// every load" fail-closed rule as SetupAdmin - the server-side TenantZeroIsEmptyAsync
        /// gate is the REAL guard (see TenantApiController.ImportAsSentinel), this is just so the
        /// form does not sit there invitingly once someone HAS signed in.</summary>
        public async Task<ActionResult> ImportSentinel()
        {
            if (!await BootstrapPendingSafeAsync())
            {
                return RedirectToAction(nameof(Index));
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ImportSentinel(TenantSentinelImportViewModel value)
        {
            if (!await BootstrapPendingSafeAsync())
            {
                return RedirectToAction(nameof(Index));
            }
            if (!ModelState.IsValid)
            {
                return View(value);
            }

            TenantExport? export;
            try
            {
                export = JsonSerializer.Deserialize<TenantExport>(value.ExportJson ?? "", ImportJsonOptions);
            }
            catch (JsonException ex)
            {
                ModelState.AddModelError(nameof(value.ExportJson), "Not valid export JSON: " + ex.Message);
                return View(value);
            }
            if (export is null)
            {
                ModelState.AddModelError(nameof(value.ExportJson), "Not valid export JSON.");
                return View(value);
            }

            try
            {
                await api.TenantImportAsSentinel(export);
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Body);
                return View(value);
            }

            TempData["Message"] = "Tenant imported - you can now sign in with one of its accounts (a new password is required on first login).";
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
