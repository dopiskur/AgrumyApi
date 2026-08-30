using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    // #66 Phase 2: groups still drive the legacy role mapping, so editing them is privilege
    // management - admins only, not every user-manager (see api's UserGroupAdd/Delete).
    [Authorize(Roles = RoleNames.Admins)]
    public class GroupController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.UserGroupsGet());

        public async Task<ActionResult> Details(int idUserGroup) =>
            View(await api.UserGroupGet(idUserGroup));

        public async Task<ActionResult> Create() =>
            View(new GroupView { UserRoles = await api.UserRoleGet() });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(GroupView groupView)
        {
            if (!ModelState.IsValid)
            {
                groupView.UserRoles = await api.UserRoleGet();
                return View(groupView);
            }

            await api.UserGroupAdd(groupView.UserGroup);
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
