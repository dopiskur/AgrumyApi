using api.Dal.Interface;
using api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [Authorize(Roles = "admin")]
    public class GroupController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.UserGroupsGet());

        public async Task<ActionResult> Details(int idUserGroup) =>
            View(await api.UserGroupGet(idUserGroup));

        public async Task<ActionResult> Create() =>
            View(new GroupView { UserRoles = await api.UserRoleGet() });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(UserGroup userGroup)
        {
            await api.UserGroupAdd(userGroup);
            return RedirectToAction(nameof(Index));
        }

        public async Task<ActionResult> Delete(int idUserGroup) =>
            View(await api.UserGroupGet(idUserGroup));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirm(int? idUserGroup)
        {
            await api.UserGroupDelete(idUserGroup);
            return RedirectToAction(nameof(Index));
        }
    }
}
