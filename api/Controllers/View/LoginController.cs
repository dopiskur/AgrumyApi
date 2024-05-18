using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MySqlX.XDevAPI;
using Org.BouncyCastle.Bcpg.OpenPgp;
using System.Net;

namespace api.Controllers.View
{
    public class LoginController : Controller
    {

        private static string? secureKey = Config.secureKey;
        const string CookieUserId = "userID";
        const string CookieLogin = "login";
        const string CookieAuthorization = "authorization";

        // GET: LoginController
        public ActionResult Index()
        {



            UserLogin userLogin = new UserLogin();
            return View(userLogin);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Index(UserLogin userLogin)
        {

            try
            {
                UserLoginResult? result = RepoFactory.GetApi().UserLogin(userLogin).Result;

                // login result mora vratiti ID, EMAIL, TOKEN!
                CookieOptions options = new CookieOptions();
                options.Expires = DateTime.Now.AddDays(7);
                options.HttpOnly = true;
                Response.Cookies.Append(CookieUserId, result.IDUser.ToString(), options);
                Response.Cookies.Append(CookieLogin, result.Email.ToString(), options);
                Response.Cookies.Append(CookieAuthorization, result.Token.ToString(), options);


                return RedirectToAction("Index", "Device");
            }
            catch (Exception e)
            {

                return View(userLogin);
            }
        }

        // GET: LoginController/Details/5
        public ActionResult Logout()
        {
            // Delete the cookie from the browser.
            Response.Cookies.Delete(CookieUserId);
            Response.Cookies.Delete(CookieLogin);
            Response.Cookies.Delete(CookieAuthorization);

            HttpContext.Response.Cookies.Append(CookieUserId, "");
            HttpContext.Response.Cookies.Append(CookieLogin, "");
            HttpContext.Response.Cookies.Append(CookieAuthorization, "");
            //return "Cookies are Deleted";
            //Response.Cookies["Expires"].Value = DateTime.Now.AddDays(-1);
            //HttpCookie nameCookie = Request.Cookies["Name"];

            //cookie.Expires = DateTime.Now.AddDays(-1);
            return RedirectToAction("Index", "Home");
        }


        public ActionResult Register()
        {

            return View();
        }



        // POST: LoginController/Create
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
            //Accessing the Cookie Data inside a Method
            //int? UserId = Convert.ToInt32(Request.Cookies[CookieUserId]);
            //string? UserName = Request.Cookies[CookieUserName];
            //string? Authorization = Request.Cookies["authorization"];
            //string Message = $"UserName: {UserName}, UserId: {UserId}, {Authorization}";

            return View();
        }

    }
}
