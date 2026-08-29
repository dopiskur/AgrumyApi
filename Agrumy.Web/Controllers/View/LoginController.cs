using System.Security.Claims;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [AllowAnonymous]
    public class LoginController(IApi api) : Controller
    {
        public ActionResult Index() => View(new UserLogin());

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

            if (result?.Token is null || role is null)
            {
                ModelState.AddModelError(string.Empty, "Invalid email/username or password.");
                return View(userLogin);
            }

            // HttpOnly, SameSite=Strict cookie holding an encrypted ticket - the raw JWT rides
            // along as a stored token for BearerTokenHandler, never exposed to page script.
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
            props.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = result.Token } });

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), props);

            return RedirectToAction("Index", "Device");
        }

        public async Task<ActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Login");
        }

        public ActionResult Register() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(IFormCollection collection) => RedirectToAction(nameof(Index));
    }
}
