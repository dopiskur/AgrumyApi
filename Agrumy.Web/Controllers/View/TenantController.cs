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

        /// Streams the ZIP export straight through as a browser download, never written to this server's disk (see TenantApiController.Export - contains password hashes, device ApiKeys); narrowed to GlobalAdmin since a Global reader must not pull credentials out.
        [Authorize(Roles = RoleNames.GlobalAdmin)]
        public async Task<ActionResult> Export(int idTenant, bool includeSensorData = false)
        {
            HttpResponseMessage response = await api.TenantExport(idTenant, includeSensorData);
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode);
            }
            string downloadName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? "agrumy-tenant-export.zip";
            return File(await response.Content.ReadAsStreamAsync(), "application/zip", downloadName);
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

            string exportJson;
            if (value.ExportFile is { Length: > 0 } file)
            {
                try
                {
                    exportJson = await TenantExportZipReader.ReadExportJsonAsync(file);
                }
                catch (InvalidDataException)
                {
                    ModelState.AddModelError(nameof(value.ExportFile), $"Not a valid export ZIP - missing {TenantExport.ExportEntryName}.");
                    return View(value);
                }
            }
            else if (!string.IsNullOrWhiteSpace(value.ExportJson))
            {
                exportJson = value.ExportJson;
            }
            else
            {
                ModelState.AddModelError(nameof(value.ExportFile), "Choose an export .zip file, or paste its export.json contents below.");
                return View(value);
            }

            TenantExport? export;
            try
            {
                export = JsonSerializer.Deserialize<TenantExport>(exportJson, ExportJsonOptions);
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
