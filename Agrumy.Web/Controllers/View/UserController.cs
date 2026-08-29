using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [Authorize(Roles = "admin")]
    public class UserController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.UsersGet());

        public async Task<ActionResult> Details(int? idUser) =>
            View(await api.UserGet(idUser, null, null));

        public async Task<ActionResult> Create() =>
            View(new UserView { UserGroups = await api.UserGroupsGet() });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(UserView userView)
        {
            await api.UserAdd(userView.UserAdd!);
            return RedirectToAction(nameof(Index));
        }

        public async Task<ActionResult> Edit(int? idUser)
        {
            var user = await api.UserGet(idUser, null, null);
            return View(new UserView
            {
                UserUpdate = new UserUpdate
                {
                    IDUser = user.IDUser,
                    TenantID = user.TenantID,
                    Email = user.Email,
                    DevicePin = user.DevicePin,
                    Username = user.Username,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Phone = user.Phone,
                    UserGroupID = user.UserGroupID,
                    Enabled = user.Enabled ?? false,
                },
                UserGroups = await api.UserGroupsGet(),
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(UserView userView)
        {
            await api.UserUpdate(userView.UserUpdate!);
            return View("Details", await api.UserGet(userView.UserUpdate!.IDUser, null, null));
        }

        public async Task<ActionResult> Delete(int? idUser) =>
            View(await api.UserGet(idUser, null, null));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirm(int? idUser)
        {
            await api.UserDelete(idUser);
            return RedirectToAction(nameof(Index));
        }
    }
}
