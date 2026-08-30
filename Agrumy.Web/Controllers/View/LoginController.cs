using System.Security.Claims;
using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [AllowAnonymous]
    public class LoginController(IApi api, IAuthApi authApi) : Controller
    {
        public ActionResult Index(bool sessionExpired = false)
        {
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
            string? role;
            try
            {
                result = await api.UserLogin(userLogin);
                role = result?.Token is { } token ? JwtTokenProvider.ValidateToken(token) : null;
            }
            catch (Exception)
            {
                result = null;
                role = null;
            }

            if (result?.Token is null || result.RefreshToken is null || role is null)
            {
                ModelState.AddModelError(string.Empty, "Invalid email/username or password.");
                return View(userLogin);
            }

            // HttpOnly, SameSite=Strict cookie holding an encrypted ticket - the raw JWT and refresh
            // token ride along as stored tokens for BearerTokenHandler, never exposed to page script.
            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, result.Email ?? ""),
                    new Claim(ClaimTypes.Role, role),
                },
                CookieAuthenticationDefaults.AuthenticationScheme);

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

            return RedirectToAction("Index", "Device");
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

        public ActionResult Register() => View(new UserRegistration());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(UserRegistration value)
        {
            if (!ModelState.IsValid)
            {
                return View(value);
            }

            try
            {
                await api.UserRegister(value);
            }
            catch (ApiException ex)
            {
                // e.g. "email already registered" / "username already registered"
                ModelState.AddModelError(string.Empty, ex.Body);
                return View(value);
            }

            TempData["Message"] = "Account created. An administrator has to enable it before you can sign in.";
            return RedirectToAction(nameof(Index));
        }
    }
}
