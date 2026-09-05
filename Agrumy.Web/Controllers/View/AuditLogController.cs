using api.Dal.Interface;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.View
{
    /// Read-only - the API itself already scopes the result to the caller's tenant (or every tenant for a Global admin), nothing further to decide here.
    [Authorize(Roles = RoleNames.Admins)]
    public class AuditLogController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(await api.AuditLogGet());
    }
}
