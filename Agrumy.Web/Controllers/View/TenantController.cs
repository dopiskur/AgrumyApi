using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    [Authorize(Roles = RoleNames.GlobalAdminOrReader)]
    public class TenantController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.TenantsGet());

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        public ActionResult Create() => View(new Tenant());

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create(Tenant tenant)
        {
            if (!ModelState.IsValid)
            {
                return View(tenant);
            }
            await api.TenantAdd(tenant);
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        public async Task<ActionResult> Edit(int idTenant) => View(await api.TenantGet(idTenant));

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(Tenant tenant)
        {
            if (!ModelState.IsValid)
            {
                return View(tenant);
            }
            await api.TenantUpdate(tenant);
            return RedirectToAction(nameof(Index));
        }
    }
}
