using api.Models;
using api.Security;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;


namespace api.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {

            //HANDLE SESSION
            var cookie = "";
            HttpContext.Request.Cookies.TryGetValue("authorization", out cookie);
            if (cookie == null || JwtTokenProvider.ValidateToken(cookie) == null) { return RedirectToAction("Index", "Login"); }


            return RedirectToAction("Index","Device");
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
