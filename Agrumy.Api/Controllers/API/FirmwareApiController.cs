using api.Dal.Interface;
using api.Firmware;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace api.Controllers.API
{
    /// The firmware catalog and its population paths - every write is Global admin only (install-wide, same rule as ServerConfigApiController), reads are open to device managers for the per-device update UI, Download is anonymous on purpose (see its own comment).
    [Route("/api/Firmware")]
    public class FirmwareApiController(IRepository repo, ICache cache, FirmwareCatalogService catalog, IOptions<AgrumySettings> settings) : ApiControllerBase(repo, cache)
    {
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet]
        public async Task<ActionResult<IList<DeviceFirmware>>> List(string? board) =>
            Ok(string.IsNullOrWhiteSpace(board) ? await catalog.ListAsync() : await catalog.ListForBoardAsync(board));

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpPost("Sync")]
        public async Task<ActionResult<FirmwareSyncResult>> Sync([FromBody] FirmwareSyncRequest request, CancellationToken cancellationToken)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Firmware catalog changes require the Global admin role");
            }
            return Ok(await catalog.SyncAsync(request.Mode, PublicBaseUrl, cancellationToken));
        }

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpPost("Import")]
        public async Task<ActionResult<FirmwareSyncResult>> Import([FromBody] FirmwareImportRequest request, CancellationToken cancellationToken)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Firmware catalog changes require the Global admin role");
            }
            return Ok(await catalog.ImportFromDirectoryAsync(request.Path, PublicBaseUrl, cancellationToken));
        }

        /// Multipart upload of one release-convention .bin - 4 MB is well above any ESP32 app partition (esp32dev's is 1.28 MB), so a wrong file is rejected before it is even read.
        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpPost("Upload")]
        [RequestSizeLimit(4 * 1024 * 1024)]
        public async Task<ActionResult<DeviceFirmware>> Upload(IFormFile file, CancellationToken cancellationToken)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Firmware catalog changes require the Global admin role");
            }
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }
            await using Stream content = file.OpenReadStream();
            (DeviceFirmware? firmware, string? error) = await catalog.UploadAsync(file.FileName, content, PublicBaseUrl, cancellationToken);
            return error != null ? BadRequest(error) : Ok(firmware);
        }

        /// ZIP upload from "Build from GitHub repository" (this server's or another's) - reuses ImportFromDirectoryAsync's validation, so a bigger cap than the single-.bin Upload above (multiple boards/versions in one archive).
        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpPost("UploadZip")]
        [RequestSizeLimit(64 * 1024 * 1024)]
        public async Task<ActionResult<FirmwareSyncResult>> UploadZip(IFormFile file, CancellationToken cancellationToken)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Firmware catalog changes require the Global admin role");
            }
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }
            await using Stream content = file.OpenReadStream();
            return Ok(await catalog.UploadZipAsync(content, PublicBaseUrl, cancellationToken));
        }

        /// Packages the visible catalog (+ manifest.json) into a ZIP for download - same read-access level as Manifest/Fetch below, since this only repackages what those already expose.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet("DownloadZip")]
        public async Task<ActionResult> DownloadZip(bool latestOnly, CancellationToken cancellationToken)
        {
            (Stream content, string fileName) = await catalog.BuildDownloadZipAsync(latestOnly, PublicBaseUrl, cancellationToken);
            return File(content, "application/zip", fileName);
        }

        [Authorize(Roles = RoleNames.GlobalAdmin)]
        [HttpDelete]
        public async Task<ActionResult> Delete(int idDeviceFirmware)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Firmware catalog changes require the Global admin role");
            }
            return await catalog.DeleteAsync(idDeviceFirmware) ? Ok() : NotFound();
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet("Manifest")]
        public async Task<ActionResult<FirmwareManifest>> Manifest() => Ok(await catalog.BuildManifestAsync(PublicBaseUrl));

        /// The browser "Build offline repo" tool reads every catalog file through here (same-origin via Agrumy.Web's proxy) instead of hitting GitHub directly - a release asset's redirect target does not answer cross-origin fetches from a page.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [EnableRateLimiting("device-data")]
        [HttpGet("Fetch")]
        public async Task<ActionResult> Fetch(string fileName, CancellationToken cancellationToken)
        {
            var opened = await catalog.OpenAsync(fileName, cancellationToken);
            if (opened == null)
            {
                return NotFound();
            }
            return File(opened.Value.Content, "application/octet-stream", opened.Value.FileName);
        }

        /// The Local repository's OTA download, anonymous because DeviceController::firmwareUpdate's OTA GET carries no auth headers (a .bin is public, like a GitHub release asset) - the file name is validated against the release convention (FirmwareStorage.PathFor) so the path can never leave the storage directory.
        [AllowAnonymous]
        [EnableRateLimiting("device-data")]
        [HttpGet("Download/{fileName}")]
        public ActionResult Download(string fileName)
        {
            // The same flat store also holds full-image files (FirmwareStorage.PathFor accepts either convention) - this direct download stays available for both.
            if (!FirmwareVersion.TryParseFileName(fileName, out _, out _) &&
                !FirmwareVersion.TryParseFullImageFileName(fileName, out _, out _))
            {
                return NotFound();
            }
            FirmwareStorage storage = HttpContext.RequestServices.GetRequiredService<FirmwareStorage>();
            if (!storage.Exists(fileName))
            {
                return NotFound();
            }
            // PhysicalFile sets Content-Length - the firmware aborts on a missing/zero size.
            return PhysicalFile(storage.PathFor(fileName), "application/octet-stream", fileName);
        }

        /// Base URL devices reach this API on - WebView:ApiService when configured, else whatever host this request came in on.
        private string PublicBaseUrl =>
            string.IsNullOrWhiteSpace(settings.Value.ApiService) ? $"{Request.Scheme}://{Request.Host}" : settings.Value.ApiService;
    }
}
