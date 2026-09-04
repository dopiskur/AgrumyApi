using api.Dal.Interface;
using api.Migration;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// <summary>Tenant Management CRUD (roadmap #196). Read is Global admin or Global reader; write
    /// (create/rename) is Global admin only - a Tenant scope has no meaningful self-management of
    /// its own existence, so this never delegates to a TenantAdmin the way Device/User management does.
    /// [Authorize(Roles=...)] attributes stay at the wide legacy-friendly net (same reasoning as
    /// ServerConfigApiController); the precise decision is the inline CallerIsGlobalAdmin/GlobalReader check.</summary>
    [Route("/api/Tenant")]
    public class TenantApiController(IRepository repo, ICache cache, TenantExportService exportService, TenantImportService importService) : ApiControllerBase(repo, cache)
    {
        [Authorize(Roles = "admin," + RoleNames.GlobalReader)]
        [HttpGet("All")]
        public async Task<ActionResult<IList<Tenant>>> TenantsGet()
        {
            if (!CallerIsGlobalAdmin && !CallerHasRole(RoleNames.GlobalReader))
            {
                return StatusCode(403, "Tenant Management requires the Global admin or Global reader role");
            }
            return Ok(await Repo.TenantsGetAllAsync());
        }

        [Authorize(Roles = "admin," + RoleNames.GlobalReader)]
        [HttpGet]
        public async Task<ActionResult<Tenant>> TenantGet(int idTenant)
        {
            if (!CallerIsGlobalAdmin && !CallerHasRole(RoleNames.GlobalReader))
            {
                return StatusCode(403, "Tenant Management requires the Global admin or Global reader role");
            }
            Tenant? tenant = await Repo.TenantGetByIdAsync(idTenant);
            return tenant is null ? NotFound() : Ok(tenant);
        }

        [Authorize(Roles = "admin")]
        [HttpPost]
        public async Task<ActionResult<int>> TenantAdd([FromBody] Tenant tenant)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Creating a tenant requires the Global admin role");
            }
            if (string.IsNullOrWhiteSpace(tenant.TenantName))
            {
                return BadRequest("Tenant name is required.");
            }
            return Ok(await Repo.TenantAddAsync(tenant.TenantName.Trim()));
        }

        [Authorize(Roles = "admin")]
        [HttpPut]
        public async Task<ActionResult> TenantUpdate([FromBody] Tenant tenant)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Renaming a tenant requires the Global admin role");
            }
            if (tenant.IDTenant is null)
            {
                return BadRequest("IDTenant is required.");
            }
            if (string.IsNullOrWhiteSpace(tenant.TenantName))
            {
                return BadRequest("Tenant name is required.");
            }
            tenant.TenantName = tenant.TenantName.Trim();
            await Repo.TenantUpdateAsync(tenant);
            return Ok();
        }

        // ---- Export/Import --------------------------------------------------

        /// <summary>SENSITIVE: the response carries every exported user's password hash/salt and
        /// every exported device's ApiKey - handle it like any other credential bundle (do not
        /// email it unencrypted, do not commit it to a repo, etc.). Never persisted server-side -
        /// built in memory and streamed straight back. A TenantAdmin may export only their OWN
        /// tenant (CallerTenantId); Global admin may export any.</summary>
        [Authorize(Roles = RoleNames.Admins)]
        [HttpGet("Export")]
        public async Task<ActionResult<TenantExport>> Export(int idTenant, bool includeSensorData = false, DateTime? sensorDataSinceUtc = null)
        {
            if (!CallerIsGlobalAdmin && !(CallerHasRole(RoleNames.TenantAdmin) && CallerTenantId == idTenant))
            {
                return StatusCode(403, "Exporting a tenant requires being its Tenant admin, or Global admin.");
            }
            return Ok(await exportService.ExportAsync(idTenant, includeSensorData, sensorDataSinceUtc));
        }

        /// <summary>ByName only - see api.Models.TenantImportTarget. Global admin only: unlike
        /// Export, this can create a brand-new tenant or add into one the caller doesn't already
        /// administer, so it stays at the same "Global admin only" bar as TenantAdd/TenantUpdate above.</summary>
        [Authorize(Roles = "admin")]
        [HttpPost("Import")]
        public async Task<ActionResult<TenantImportResult>> Import([FromBody] TenantImportRequest value)
        {
            if (!CallerIsGlobalAdmin)
            {
                return StatusCode(403, "Importing a tenant requires the Global admin role");
            }
            if (value.Export is null)
            {
                return BadRequest("Export is required.");
            }
            if (string.IsNullOrWhiteSpace(value.TargetTenantName))
            {
                return BadRequest("TargetTenantName is required.");
            }
            if (value.Export.FormatVersion != TenantExport.CurrentFormatVersion)
            {
                return BadRequest($"Unsupported export format version '{value.Export.FormatVersion}' - this server understands '{TenantExport.CurrentFormatVersion}'.");
            }
            return Ok(await importService.ImportByNameAsync(value.Export, value.TargetTenantName.Trim()));
        }

        /// <summary>AsSentinel: claims TenantID=0 with this export's users/devices, replacing the
        /// still-unclaimed bootstrap admin placeholder - see TenantImportService.ImportAsSentinelAsync
        /// and ITenantRepository.TenantZeroIsEmptyAsync for the safety gate. Deliberately anonymous:
        /// the whole point is a brand-new self-hosted server with nobody to authenticate as yet -
        /// TenantZeroIsEmptyAsync (not a role check) is what stops this being called against an
        /// already-provisioned server.</summary>
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
        [HttpPost("ImportAsSentinel")]
        public async Task<ActionResult<TenantImportResult>> ImportAsSentinel([FromBody] TenantExport value)
        {
            if (value.FormatVersion != TenantExport.CurrentFormatVersion)
            {
                return BadRequest($"Unsupported export format version '{value.FormatVersion}' - this server understands '{TenantExport.CurrentFormatVersion}'.");
            }
            var (result, error) = await importService.ImportAsSentinelAsync(value);
            return error != null ? StatusCode(409, error) : Ok(result);
        }
    }
}
