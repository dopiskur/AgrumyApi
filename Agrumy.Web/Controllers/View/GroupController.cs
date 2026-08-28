using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    public class GroupController : Controller
    {
        public ActionResult Index()
        {

            string? roleName;
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            IEnumerable<UserGroup> userGroup = RepoFactory.GetApi().UserGroupsGet(jwtKey).Result;

            return View(userGroup);
        }

        public ActionResult Details(int idUserGroup)
        {
            string? roleName;
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            UserGroup userGroup = RepoFactory.GetApi().UserGroupGet(jwtKey, idUserGroup).Result;

            return View(userGroup);
        }

        public ActionResult Create()
        {
            string? roleName;
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            GroupView? groupView = new GroupView();
            groupView.UserRoles = RepoFactory.GetApi().UserRoleGet(jwtKey).Result;

            return View(groupView);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(UserGroup userGroup)
        {
            try
            {
                string? roleName;
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                RepoFactory.GetApi().UserGroupAdd(jwtKey, userGroup);
                


                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }


        public ActionResult Delete(int idUserGroup)
        {
            string? roleName;
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            UserGroup userGroup = RepoFactory.GetApi().UserGroupGet(jwtKey, idUserGroup).Result;
            return View(userGroup);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirm(int? idUserGroup)
        {
            try
            {
                string? roleName;
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                bool result = RepoFactory.GetApi().UserGroupDelete(jwtKey, idUserGroup).Result;
                return RedirectToAction(nameof(Index));
            }
            catch (Exception e)
            {

                return StatusCode(500, e.Message);
            }
        }
    }
}
