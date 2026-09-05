using System.Text;
using System.Text.Json;
using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.Controllers.View
{
    [Authorize(Roles = RoleNames.GlobalAdminOrReader)]
    public class TenantController(IApi api) : Controller
    {
        // Human-readable, same convention as DeviceUnitZoneRule.ConditionConfig - an admin may open this JSON to sanity-check it before importing elsewhere.
        private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

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
        public async Task<ActionResult> Edit(int idTenant)
        {
            Tenant tenant = await api.TenantGet(idTenant);
            ViewBag.TimeZones = TimeZoneOptions(tenant.ScheduleTimeZone);
            return View(tenant);
        }

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit(Tenant tenant)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.TimeZones = TimeZoneOptions(tenant.ScheduleTimeZone);
                return View(tenant);
            }
            try
            {
                await api.TenantUpdate(tenant);
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(nameof(Tenant.ScheduleTimeZone), ex.Body);
                ViewBag.TimeZones = TimeZoneOptions(tenant.ScheduleTimeZone);
                return View(tenant);
            }
            return RedirectToAction(nameof(Index));
        }

        // A blank ScheduleTimeZone is a real, intentional state (schedules evaluate as UTC), unlike a user's display TimeZone.
        private static List<SelectListItem> TimeZoneOptions(string? selected)
        {
            var options = new List<SelectListItem> { new("(not set - schedules evaluate as UTC)", "") };
            options.AddRange(TimeZoneHelper.GetTimeZoneOptions()
                .Select(o => new SelectListItem(o.DisplayName, o.Id, string.Equals(o.Id, selected, StringComparison.OrdinalIgnoreCase))));
            return options;
        }

        // ---- Export/Import --------------------------------------------------

        /// Streams the export as a browser download, never written to this server's disk (see TenantApiController.Export - contains password hashes, device ApiKeys); narrowed to GlobalAdmin since a Global reader must not pull credentials out.
        [Authorize(Roles = RoleNames.GlobalAdmin)]
        public async Task<ActionResult> Export(int idTenant, bool includeSensorData = false)
        {
            Tenant tenant = await api.TenantGet(idTenant);
            TenantExport export = await api.TenantExport(idTenant, includeSensorData);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(export, ExportJsonOptions));
            string fileName = $"agrumy-tenant-{(tenant.TenantName ?? "export").ToLowerInvariant().Replace(' ', '-')}-{DateTime.UtcNow:yyyyMMdd}.json";
            return File(bytes, "application/json", fileName);
        }

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        public ActionResult Import() => View(new TenantImportViewModel());

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Import(TenantImportViewModel value)
        {
            if (!ModelState.IsValid)
            {
                return View(value);
            }

            TenantExport? export;
            try
            {
                export = JsonSerializer.Deserialize<TenantExport>(value.ExportJson ?? "", ExportJsonOptions);
            }
            catch (JsonException ex)
            {
                ModelState.AddModelError(nameof(value.ExportJson), "Not valid export JSON: " + ex.Message);
                return View(value);
            }
            if (export is null)
            {
                ModelState.AddModelError(nameof(value.ExportJson), "Not valid export JSON.");
                return View(value);
            }

            try
            {
                value.Result = await api.TenantImport(new TenantImportRequest { Export = export, TargetTenantName = value.TargetTenantName });
            }
            catch (ApiException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Body);
                return View(value);
            }

            return View(value);
        }
    }
}
