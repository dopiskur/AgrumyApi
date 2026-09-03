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
            // Roadmap #91: checked fresh on every load (not cached anywhere) so this redirect stops
            // firing the instant SetupAdmin below succeeds - see BootstrapPendingSafeAsync.
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
                // An unreachable/broken API must be distinguishable from a wrong password in the log -
                // both render the same generic message below (see roadmap #48 debugging history).
                logger.LogError(ex, "Login call to Agrumy.Api failed.");
                result = null;
                roles = null;
            }

            if (result?.Token is null || result.RefreshToken is null || roles is null || roles.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Invalid email/username or password.");
                return View(userLogin);
            }

            // HttpOnly, SameSite=Strict cookie holding an encrypted ticket - the raw JWT and refresh
            // token ride along as stored tokens for BearerTokenHandler, never exposed to page script.
            // Roadmap #66: a caller can hold several roles at once - one Role claim per entry, same
            // as the JWT itself; User.IsInRole(...) checks across all of them regardless of count.
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
            // Best effort: kill the refresh token server-side so it can't be redeemed after logout.
            // Must never block the local sign-out - a dead/unreachable API is not a reason to trap
            // the user in a "logged in" cookie.
            string? refreshToken = await HttpContext.GetTokenAsync("refresh_token");
            if (!string.IsNullOrEmpty(refreshToken))
            {
                try
                {
                    await authApi.RevokeRefreshToken(new RefreshTokenRequest { RefreshToken = refreshToken });
                }
                catch (Exception)
                {
                    // ignored - local sign-out below still proceeds
                }
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Login");
        }

        /// <summary>Roadmap #91: first-run "set password" screen for the bootstrap Global Admin.
        /// CRITICAL - re-checks BootstrapPending on every load (GET and POST alike, not just once at
        /// the Index redirect above) and bounces to the normal login form the moment it's false, so
        /// this can never become a standing unauthenticated password-reset route once the real admin
        /// account has a password.</summary>
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

        /// <summary>Fails closed to the normal login form on any error - an unreachable API is not a
        /// reason to show (or hide) the unauthenticated set-password screen by guesswork, same
        /// reasoning as SetTenantCreationViewBagAsync below.</summary>
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
                // e.g. "email already registered" / "unknown tenant name" (roadmap #64)
                ModelState.AddModelError(string.Empty, ex.Body);
                await SetTenantCreationViewBagAsync();
                return View(value);
            }

            // Roadmap #24/#63: Enabled alone was never the real gate (see roadmap #68) - email
            // verification comes first for everyone, tenant-admin approval only after that and only
            // for someone joining an existing, non-default tenant. Kept generic here since which of
            // those applies depends on server-side state (tenant 0? brand new tenant?) this page
            // doesn't have visibility into.
            TempData["Message"] = "Account created. Check your email for a verification link - " +
                "depending on your tenant, you may also need an administrator's approval before you can sign in.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Roadmap #64: the Register view only offers a tenant-name field (and the "create
        /// a new tenant" hint) when self-service tenant creation is on - fails closed (hides the
        /// option) if the API call itself fails, same reasoning as any other anonymous-page best-effort call.</summary>
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
