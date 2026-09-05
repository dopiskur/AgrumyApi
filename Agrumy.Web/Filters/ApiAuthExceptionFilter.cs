using api.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace api.Filters
{
    // A 401 here means the stored JWT expired (~2h) or was revoked; clear the now-useless cookie instead of showing an error page.
    public sealed class ApiAuthExceptionFilter : IAsyncExceptionFilter
    {
        public async Task OnExceptionAsync(ExceptionContext context)
        {
            if (context.Exception is not ApiException { StatusCode: 401, IsAuthChallenge: true })
            {
                return;
            }

            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Result = new RedirectToActionResult("Index", "Login", new { sessionExpired = true });
            context.ExceptionHandled = true;
        }
    }
}
