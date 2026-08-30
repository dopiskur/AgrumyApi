using api.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace api.Filters
{
    /// <summary>
    /// A 401 from Agrumy.Api means the stored JWT has expired (it lives ~2h, the auth cookie 7 days)
    /// or was revoked. Clear the now-useless cookie and send the user to the login page instead of
    /// letting the <see cref="ApiException"/> surface as an error page.
    /// </summary>
    public sealed class ApiAuthExceptionFilter : IAsyncExceptionFilter
    {
        public async Task OnExceptionAsync(ExceptionContext context)
        {
            if (context.Exception is not ApiException { StatusCode: 401 })
            {
                return;
            }

            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result = new RedirectToActionResult("Index", "Login", new { sessionExpired = true });
            context.ExceptionHandled = true;
        }
    }
}
