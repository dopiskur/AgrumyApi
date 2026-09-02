using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Utils;
using api.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StreamPart = Refit.StreamPart; // not `using Refit;` - its AuthorizeAttribute clashes with ASP.NET's

namespace api.Controllers.View
{
    /// <summary>Roadmap #94: the firmware catalog page and its population actions. Global admin
    /// only, same reasoning as ServerConfigController - the catalog is install-wide. The per-device
    /// "update this device" actions live on DeviceController (device managers).</summary>
    [Authorize(Roles = RoleNames.GlobalAdmin)]
    public class FirmwareController(IApi api) : Controller
    {
        public async Task<ActionResult> Index() => View(new FirmwareViewModel
        {
            Config = await api.ServerConfigGet(),
            Catalog = await api.FirmwareList(null),
        });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Sync(FirmwareSyncMode mode)
        {
            await RunAndReport(() => api.FirmwareSync(new FirmwareSyncRequest { Mode = mode }));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Import(string path)
        {
            await RunAndReport(() => api.FirmwareImport(new FirmwareImportRequest { Path = path }));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Upload(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Choose a .bin file first.";
                return RedirectToAction(nameof(Index));
            }
            try
            {
                await using Stream stream = file.OpenReadStream();
                DeviceFirmware added = await api.FirmwareUpload(new StreamPart(stream, file.FileName, "application/octet-stream"));
                TempData["Message"] = $"Uploaded {added.FileName} ({added.Board} {added.Version}).";
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Delete(int idDeviceFirmware)
        {
            await api.FirmwareDelete(idDeviceFirmware);
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Roadmap #94-C1: same-origin JSON for offline-repo.js (the browser cannot call
        /// Agrumy.Api directly - different origin, and the JWT lives in this app's cookie).</summary>
        public async Task<ActionResult> OfflineManifest() => Json(await api.FirmwareManifest());

        /// <summary>Roadmap #94-C1: streams one catalog file to the browser tool through this app
        /// and the API, so GitHub-hosted assets never need cross-origin fetch permission.</summary>
        public async Task<ActionResult> OfflineFile(string fileName)
        {
            HttpResponseMessage response = await api.FirmwareFetch(fileName);
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode);
            }
            string downloadName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? fileName;
            return File(await response.Content.ReadAsStreamAsync(), "application/octet-stream", downloadName);
        }

        private async Task RunAndReport(Func<Task<FirmwareSyncResult>> action)
        {
            try
            {
                FirmwareSyncResult result = await action();
                TempData["Message"] = $"Done: {result.Added} added, {result.Skipped} skipped, {result.Removed} removed.";
                if (result.Warnings.Count > 0)
                {
                    TempData["Error"] = string.Join(" | ", result.Warnings);
                }
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
        }
    }
}
