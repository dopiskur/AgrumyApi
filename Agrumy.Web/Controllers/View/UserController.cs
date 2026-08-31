using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    // #66 Phase 2: user pages open to every user-manager role, not only the legacy binary admin;
    // fine-grained tenant scoping is enforced API-side, this attribute is just the menu-level gate.
    [Authorize(Roles = RoleNames.UserManagers)]
    public class UserController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.UsersGet());

        public async Task<ActionResult> Details(int? idUser) =>
            View(await api.UserGet(idUser));

        public async Task<ActionResult> Create() =>
            View(new UserView { UserGroups = await api.UserGroupsGet() });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(UserView userView)
        {
            if (!ModelState.IsValid)
            {
                userView.UserGroups = await api.UserGroupsGet();
                return View(userView);
            }

            await api.UserAdd(userView.UserAdd!);
            return RedirectToAction(nameof(Index));
        }

        public async Task<ActionResult> Edit(int? idUser)
        {
            var user = await api.UserGet(idUser);
            return View(new UserView
            {
                UserUpdate = new UserUpdate
                {
                    IDUser = user.IDUser,
                    TenantID = user.TenantID,
                    Email = user.Email,
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
            if (!ModelState.IsValid)
            {
                userView.UserGroups = await api.UserGroupsGet();
                return View(userView);
            }

            await api.UserUpdate(userView.UserUpdate!);
            return View("Details", await api.UserGet(userView.UserUpdate!.IDUser));
        }

        /// <summary>Roadmap #66: a user can hold several roles at once - this edits the whole set,
        /// separate from Edit()'s single legacy UserGroupID field (kept for backward compatibility).
        /// Admin-only (not every user-manager) - same self-escalation reasoning as the API's UserRolesSet.</summary>
        [Authorize(Roles = RoleNames.Admins)]
        public async Task<ActionResult> Roles(int? idUser)
        {
            var user = await api.UserGet(idUser);
            var assigned = await api.UserRolesGet(idUser!.Value);
            return View(new UserRolesViewModel
            {
                IDUser = idUser.Value,
                Email = user.Email,
                AllRoles = RoleNames.All,
                AssignedRoles = assigned,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RoleNames.Admins)]
        public async Task<ActionResult> Roles(UserRolesViewModel value)
        {
            try
            {
                await api.UserRolesSet(new UserRolesUpdate { IDUser = value.IDUser, RoleNames = value.AssignedRoles.ToList() });
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Body);
                value.AllRoles = RoleNames.All;
                return View(value);
            }
            return RedirectToAction(nameof(Details), new { idUser = value.IDUser });
        }

        public async Task<ActionResult> Delete(int? idUser) =>
            View(await api.UserGet(idUser));

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirm(int? idUser)
        {
            await api.UserDelete(idUser);
            return RedirectToAction(nameof(Index));
        }
    }
}
