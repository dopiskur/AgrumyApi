using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace api.Controllers.View
{
    /// <summary>Sets the UI-language cookie read by Program.cs's RequestLocalizationOptions - reachable pre-login (_AuthLayout) as well as from the main app shell, so it must stay anonymous.</summary>
    [AllowAnonymous]
    public class LanguageController : Controller
    {
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Set(string culture, string returnUrl)
        {
            if (SupportedCultures.All.Contains(culture))
            {
                Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(CultureInfo.InvariantCulture, new CultureInfo(culture))),
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });
            }
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
        }
    }
}
