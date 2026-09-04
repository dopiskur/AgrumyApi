using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [Authorize(Roles = RoleNames.UserManagers)]
    public class UserController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.UsersGet());

        public async Task<ActionResult> Details(int? idUser) =>
            View(await api.UserGet(idUser));

        public ActionResult Create() => View(new UserView());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(UserView userView)
        {
            if (!ModelState.IsValid)
            {
                return View(userView);
            }

            await api.UserAdd(userView.UserAdd!);
            return RedirectToAction(nameof(Index));
        }

        public async Task<ActionResult> Edit(int? idUser)
        {
            var user = await api.UserGet(idUser);
            var assignedRoles = await api.UserRolesGet(idUser!.Value);
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
                    RoleNames = assignedRoles.ToList(),
                    Enabled = user.Enabled ?? false,
                },
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(UserView userView)
        {
            if (!ModelState.IsValid)
            {
                return View(userView);
            }

            await api.UserUpdate(userView.UserUpdate!);
            // PRG: redirect so a refresh re-fetches Details instead of re-submitting the update.
            return RedirectToAction(nameof(Details), new { idUser = userView.UserUpdate!.IDUser });
        }

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
