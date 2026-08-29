using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    public class GroupController : Controller
    {
        private readonly IApi _api;

        public GroupController(IApi api) => _api = api ?? throw new ArgumentNullException(nameof(api));

        public async Task<ActionResult> Index()
        {
            string? roleName;
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            IEnumerable<UserGroup> userGroup = await _api.UserGroupsGet(jwtKey);

            return View(userGroup);
        }

        public async Task<ActionResult> Details(int idUserGroup)
        {
            string? roleName;
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            UserGroup userGroup = await _api.UserGroupGet(jwtKey, idUserGroup);

            return View(userGroup);
        }

        public async Task<ActionResult> Create()
        {
            string? roleName;
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            GroupView? groupView = new GroupView();
            groupView.UserRoles = await _api.UserRoleGet(jwtKey);

            return View(groupView);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(UserGroup userGroup)
        {
            try
            {
                string? roleName;
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                await _api.UserGroupAdd(jwtKey, userGroup);

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        public async Task<ActionResult> Delete(int idUserGroup)
        {
            string? roleName;
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
            if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

            UserGroup userGroup = await _api.UserGroupGet(jwtKey, idUserGroup);
            return View(userGroup);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirm(int? idUserGroup)
        {
            try
            {
                string? roleName;
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                bool result = await _api.UserGroupDelete(jwtKey, idUserGroup);
                return RedirectToAction(nameof(Index));
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }
    }
}
