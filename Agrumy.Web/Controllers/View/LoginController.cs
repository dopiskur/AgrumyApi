using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Mvc;


namespace api.Controllers.View
{
    public class LoginController : Controller
    {
        private const string CookieUserId = "userID";
        private const string CookieLogin = "login";
        private const string CookieAuthorization = "authorization";

        private readonly IApi _api;

        public LoginController(IApi api) => _api = api ?? throw new ArgumentNullException(nameof(api));

        public ActionResult Index()
        {
            UserLogin userLogin = new UserLogin();
            return View(userLogin);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Index(UserLogin userLogin)
        {
            try
            {
                UserLoginResult? result = await _api.UserLogin(userLogin);
                if (result?.Token == null)
                {
                    ModelState.AddModelError(string.Empty, "Invalid email/username or password.");
                    return View(userLogin);
                }

                // HttpOnly keeps the JWT out of reach of any injected script (unlike localStorage,
                // which page JS - including a successful XSS payload - can read outright).
                CookieOptions options = new CookieOptions
                {
                    Expires = DateTime.Now.AddDays(7),
                    HttpOnly = true,
                    // Secure over HTTPS (production), but not over plain-HTTP dev on localhost -
                    // browsers silently drop a Secure cookie set over HTTP, which would leave the
                    // post-login redirect to /Device with no auth cookie (looks like login "does nothing").
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Strict,
                };
                Response.Cookies.Append(CookieUserId, result.IDUser.ToString() ?? "", options);
                Response.Cookies.Append(CookieLogin, result.Email ?? "", options);
                Response.Cookies.Append(CookieAuthorization, result.Token, options);

                return RedirectToAction("Index", "Device");
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "Invalid email/username or password.");
                return View(userLogin);
            }
        }

        public ActionResult Logout()
        {
            // Delete the cookie from the browser.
            Response.Cookies.Delete(CookieUserId);
            Response.Cookies.Delete(CookieLogin);
            Response.Cookies.Delete(CookieAuthorization);

            HttpContext.Response.Cookies.Append(CookieUserId, "");
            HttpContext.Response.Cookies.Append(CookieLogin, "");
            HttpContext.Response.Cookies.Append(CookieAuthorization, "");

            return RedirectToAction("Index", "Home");
        }

        public ActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        public ActionResult Cookie()
        {
            return View();
        }
    }
}
