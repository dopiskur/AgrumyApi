using System.Text.Json.Serialization;
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
    [Authorize(Roles = RoleNames.GlobalAdmin)]
    public class FirmwareController(IApi api) : Controller
    {
        public async Task<ActionResult> Index()
        {
            ServerConfig config = await api.ServerConfigGet();
            IList<DeviceFirmware> catalog = await api.FirmwareList(null);

            return View(new FirmwareViewModel
            {
                Config = config,
                Catalog = catalog,
                InstallableBoards = InstallableBoards(catalog, config),
            });
        }

        private static List<DeviceFirmware> InstallableBoards(IList<DeviceFirmware> catalog, ServerConfig config) =>
            catalog
                .Where(f => f.Board != null && f.FullImageFileName != null && (f.Source == config.FirmwareSource || f.Source == FirmwareSource.Local))
                .GroupBy(f => f.Board!)
                .Select(g => g.First())
                .ToList();

        // ServerConfigApiController.Update overwrites the whole row, so this fetches a fresh copy
        // and only overlays the three fields this form actually shows - anything else changed
        // concurrently on the Server Settings page would otherwise get clobbered back to whatever
        // this page's stale model had.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SaveSettings(FirmwareSource firmwareSource, string? firmwareGitHubRepository, string? firmwareCustomRepositoryUrl, int? firmwareRefreshIntervalHours)
        {
            ServerConfig config = await api.ServerConfigGet();
            config.FirmwareSource = firmwareSource;
            config.FirmwareGitHubRepository = firmwareGitHubRepository;
            config.FirmwareCustomRepositoryUrl = firmwareCustomRepositoryUrl;
            config.FirmwareRefreshIntervalHours = firmwareRefreshIntervalHours;
            try
            {
                await api.ServerConfigUpdate(config);
                TempData["Message"] = "Firmware source settings saved.";
            }
            catch (ApiException ex)
            {
                TempData["Error"] = ex.Body;
            }
            return RedirectToAction(nameof(Index));
        }

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

        public async Task<ActionResult> OfflineManifest() => Json(await api.FirmwareManifest());

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

        public async Task<ActionResult> InstallManifest(string board)
        {
            string? chipFamily = EspChipFamily.ForBoard(board);
            if (chipFamily == null)
            {
                return NotFound();
            }
            DeviceFirmware? latest = (await api.FirmwareList(board)).FirstOrDefault(f => f.FullImageFileName != null);
            if (latest == null)
            {
                return NotFound();
            }
            return Json(new EspWebToolsManifest(
                $"Agrumy {board}",
                latest.Version ?? "unknown",
                NewInstallPromptErase: true,
                [new EspWebToolsBuild(chipFamily, [new EspWebToolsPart(Url.Action(nameof(OfflineFile), new { fileName = latest.FullImageFileName })!, Offset: 0)])]
            ));
        }

        private sealed record EspWebToolsManifest(string Name, string Version,
            [property: JsonPropertyName("new_install_prompt_erase")] bool NewInstallPromptErase, List<EspWebToolsBuild> Builds);
        private sealed record EspWebToolsBuild(string ChipFamily, List<EspWebToolsPart> Parts);
        private sealed record EspWebToolsPart(string Path, int Offset);

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
