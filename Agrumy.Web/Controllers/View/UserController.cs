using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Mvc;


namespace api.Controllers.View
{
    public class UserController : Controller
    {
        private readonly IApi _api;

        public UserController(IApi api) => _api = api ?? throw new ArgumentNullException(nameof(api));

        // The app has no UI for adding roles, so this list never changes at runtime - no caching
        // needed. Kept as its own method (rather than inlined) so that can change later if it does.
        private async Task<IEnumerable<UserGroup>> UserGroups()
        {
            HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
            return await _api.UserGroupsGet(jwtKey);
        }

        public async Task<ActionResult> Index()
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                IEnumerable<User> users = await _api.UsersGet(jwtKey);

                return View(users);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        public async Task<ActionResult> Details(int? idUser)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                User user = await _api.UserGet(jwtKey, idUser, null, null);

                return View(user);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        public async Task<ActionResult> Create()
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                UserView userView = new UserView();
                userView.UserGroups = await UserGroups();
                return View(userView);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(UserView userView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                UserAdd userAdd = new UserAdd();

                userAdd.TenantID = userView.UserAdd.TenantID;
                userAdd.Email = userView.UserAdd.Email;
                userAdd.Password = userView.UserAdd.Password;
                userAdd.Username = userView.UserAdd.Username;
                userAdd.FirstName = userView.UserAdd.FirstName;
                userAdd.LastName = userView.UserAdd.LastName;
                userAdd.Phone = userView.UserAdd.Phone;
                userAdd.UserGroupID = userView.UserAdd.UserGroupID;
                userAdd.Enabled = userView.UserAdd.Enabled;

                await _api.UserAdd(jwtKey, userAdd);

                return RedirectToAction("Index");
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        public async Task<ActionResult> Edit(int? idUser)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                UserView? userView = new UserView();
                User value = await _api.UserGet(jwtKey, idUser, null, null);

                UserUpdate userUpdate = new UserUpdate();
                userUpdate.IDUser = value.IDUser;
                userUpdate.TenantID = value.TenantID;
                userUpdate.Email = value.Email;
                userUpdate.DevicePin = value.DevicePin;
                userUpdate.Username = value.Username;
                userUpdate.FirstName = value.FirstName;
                userUpdate.LastName = value.LastName;
                userUpdate.Phone = value.Phone;
                userUpdate.UserGroupID = value.UserGroupID;
                userUpdate.Enabled = value.Enabled ?? false;

                userView.UserUpdate = userUpdate;
                userView.UserGroups = await UserGroups();

                return View(userView);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(UserView userView)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                UserUpdate userUpdate = userView.UserUpdate;

                await _api.UserUpdate(jwtKey, userUpdate);

                User user = await _api.UserGet(jwtKey, userView.UserUpdate.IDUser, null, null);
                return View("Details", user);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        public async Task<ActionResult> Delete(int? idUser)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                User user = await _api.UserGet(jwtKey, idUser, null, null);
                return View(user);
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirm(int? idUser)
        {
            try
            {
                HttpContext.Request.Cookies.TryGetValue("authorization", out var jwtKey);
                string? roleName;
                if (jwtKey == null || (roleName = JwtTokenProvider.ValidateToken(jwtKey)) == null) { return RedirectToAction("Index", "Login"); }
                if (roleName != "admin") { return RedirectToAction("Index", "Device"); }

                await _api.UserDelete(jwtKey, idUser);

                return RedirectToAction(nameof(Index));
            }
            catch (Exception e)
            {
                return StatusCode(500, e.Message);
            }
        }
    }
}
