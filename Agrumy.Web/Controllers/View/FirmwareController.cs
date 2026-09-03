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
            (IList<DeviceFirmware> auto, IList<DeviceFirmware> manual) = SplitInstallableBoards(catalog, config);

            return View(new FirmwareViewModel
            {
                Config = config,
                Catalog = catalog,
                AutoDetectBuilds = auto,
                ManualBuilds = manual,
            });
        }

        /// <summary>Roadmap #41: one full-image build per board that has one, latest version first
        /// (Catalog is already board-then-newest-version-first, FirmwareCatalogService.ListAsync).
        /// Roadmap #148: then split by whether esp-web-tools can safely auto-select the board from
        /// the physical chip alone - see FirmwareViewModel.AutoDetectBuilds/ManualBuilds and
        /// InstallManifestAuto below. Shared by Index (to render the buttons) and InstallManifestAuto
        /// (to build the combined manifest) so the two can never disagree about which boards qualify.</summary>
        private static (IList<DeviceFirmware> Auto, IList<DeviceFirmware> Manual) SplitInstallableBoards(
            IList<DeviceFirmware> catalog, ServerConfig config)
        {
            List<DeviceFirmware> installable = catalog
                .Where(f => f.Board != null && f.FullImageFileName != null && (f.Source == config.FirmwareSource || f.Source == FirmwareSource.Local))
                .GroupBy(f => f.Board!)
                .Select(g => g.First())
                .ToList();

            var byFamily = installable
                .Select(f => (Firmware: f, Family: EspChipFamily.ForBoard(f.Board)))
                .GroupBy(x => x.Family)
                .ToList();

            List<DeviceFirmware> auto = byFamily.Where(g => g.Key != null && g.Count() == 1)
                .SelectMany(g => g.Select(x => x.Firmware)).ToList();
            List<DeviceFirmware> manual = byFamily.Where(g => g.Key == null || g.Count() > 1)
                .SelectMany(g => g.Select(x => x.Firmware)).ToList();
            return (auto, manual);
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

        /// <summary>Roadmap #148: the combined manifest behind the single "Install (auto-detect
        /// board)" button - one `builds` entry per SplitInstallableBoards' AutoDetectBuilds board,
        /// each tagged with its own chipFamily. esp-web-tools reads the connected chip's actual
        /// family over Web Serial before flashing and matches it against these entries itself
        /// (https://esphome.github.io/esp-web-tools/) - the admin no longer has to know or guess
        /// which per-board button corresponds to the physical board in front of them. Deliberately
        /// excludes anything SplitInstallableBoards routed to ManualBuilds: chip family alone cannot
        /// tell two boards of the same family apart, so a shared/unrecognized family stays a manual,
        /// explicitly-labeled button (Index.cshtml) rather than risk silently offering the wrong
        /// board's image to an auto-detected chip.</summary>
        public async Task<ActionResult> InstallManifestAuto()
        {
            ServerConfig config = await api.ServerConfigGet();
            IList<DeviceFirmware> catalog = await api.FirmwareList(null);
            (IList<DeviceFirmware> auto, _) = SplitInstallableBoards(catalog, config);
            if (auto.Count == 0)
            {
                return NotFound();
            }

            List<EspWebToolsBuild> builds = auto
                .Select(f => new EspWebToolsBuild(
                    EspChipFamily.ForBoard(f.Board)!,
                    [new EspWebToolsPart(Url.Action(nameof(OfflineFile), new { fileName = f.FullImageFileName })!, Offset: 0)]))
                .ToList();
            // Every board in a release is normally built from the same tag (release.yml), so this is
            // almost always one shared version - "mixed" only shows up if the catalog was assembled
            // from builds at different times (e.g. an Upload replaced just one board's entry).
            string version = auto.Select(f => f.Version).Distinct().Count() == 1 ? auto[0].Version ?? "unknown" : "mixed";

            return Json(new EspWebToolsManifest("Agrumy", version, NewInstallPromptErase: true, builds));
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
