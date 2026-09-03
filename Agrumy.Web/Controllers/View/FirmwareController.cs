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
    /// <summary>Roadmap #94: the firmware catalog page and its population actions. Global admin
    /// only, same reasoning as ServerConfigController - the catalog is install-wide. The per-device
    /// "update this device" actions live on DeviceController (device managers).</summary>
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

        /// <summary>Roadmap #41: one full-image build per board that has one, latest version first
        /// (Catalog is already board-then-newest-version-first, FirmwareCatalogService.ListAsync).
        /// Roadmap #155: no longer split by chip family (that #148 grouping is removed, not
        /// extended) - the admin now always picks the board explicitly from Index.cshtml's dropdown,
        /// so esp-web-tools never has to guess between same-family boards, and InstallManifest's own
        /// single-chipFamily manifest already refuses to flash a mismatched connected chip. That
        /// refusal - not a bespoke check here - IS the "chip read as safety check" #155 asked for.</summary>
        private static List<DeviceFirmware> InstallableBoards(IList<DeviceFirmware> catalog, ServerConfig config) =>
            catalog
                .Where(f => f.Board != null && f.FullImageFileName != null && (f.Source == config.FirmwareSource || f.Source == FirmwareSource.Local))
                .GroupBy(f => f.Board!)
                .Select(g => g.First())
                .ToList();

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
        /// and the API, so GitHub-hosted assets never need cross-origin fetch permission. Roadmap
        /// #41 reuses this UNCHANGED for the web installer's full-image bytes too - it is keyed by
        /// plain file name, and FirmwareCatalogService.OpenAsync already resolves either convention.</summary>
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

        /// <summary>Roadmap #41: an esp-web-tools manifest (https://esphome.github.io/esp-web-tools/)
        /// for the latest catalog build of <paramref name="board"/> that has a full-image sibling -
        /// same-origin JSON (the browser's &lt;esp-web-install-button&gt; fetches this directly, no
        /// JWT available to it) whose one part points back at OfflineFile above, not at Agrumy.Api
        /// directly - same cross-origin reasoning as OfflineManifest/OfflineFile already document.</summary>
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

        // ---- esp-web-tools manifest shape (roadmap #41) - external tool's fixed schema, snake_case
        // on the wire (esp-web-tools reads new_install_prompt_erase literally), so this is kept
        // separate from FirmwareManifest (api.Models), which is Agrumy's OWN, unrelated JSON contract.
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
