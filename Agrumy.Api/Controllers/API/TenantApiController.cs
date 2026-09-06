using api.Dal.Interface;
using api.Migration;
using api.Models;
using api.Security;
using api.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// Tenant Management CRUD - write is Global admin only since a tenant has no meaningful self-management of its own existence, unlike Device/User management; [Authorize] stays at the wide RoleNames.LegacyAdmin/TenantReaders net (same reasoning as ServerConfigApiController), the precise decision is the inline CallerIsGlobalAdmin/GlobalReader check.
    [Route("/api/Tenant")]
    public class TenantApiController(ITenantRepository tenantRepo, IUserRepository userRepo, IAuditLogRepository auditLogRepo, ICache cache, TenantExportService exportService, TenantImportService importService) : ApiControllerBase(userRepo, auditLogRepo, cache)
    {
        [Authorize(Roles = RoleNames.TenantReaders)]
        [HttpGet("All")]
        public async Task<ActionResult<IList<Tenant>>> TenantsGet()
        {
            if (!CallerIsGlobalAdmin && !CallerHasRole(RoleNames.GlobalReader))
            {
                return StatusCode(403, "Tenant Management requires the Global admin or Global reader role");
            }
            return Ok(await tenantRepo.TenantsGetAllAsync());
        }

        [Authorize(Roles = RoleNames.TenantReaders)]
        [HttpGet]
        public async Task<ActionResult<Tenant>> TenantGet(int idTenant)
        {
            if (!CallerIsGlobalAdmin && !CallerHasRole(RoleNames.GlobalReader))
            {
                return StatusCode(403, "Tenant Management requires the Global admin or Global reader role");
            }
            Tenant? tenant = await tenantRepo.TenantGetByIdAsync(idTenant);
            return tenant is null ? NotFound() : Ok(tenant);
        }

        [Authorize(Roles = RoleNames.LegacyAdmin)]
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
            return Ok(await tenantRepo.TenantAddAsync(tenant.TenantName.Trim()));
        }

        [Authorize(Roles = RoleNames.LegacyAdmin)]
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

            // A bad id would silently degrade every device in this tenant's schedule mode to UTC (TimeZoneHelper.GetUtcOffsetSeconds' fallback) instead of failing at save time; blank/null clears it back to "not configured".
            if (!string.IsNullOrWhiteSpace(tenant.ScheduleTimeZone))
            {
                if (!TimeZoneHelper.TryNormalizeToIana(tenant.ScheduleTimeZone, out string iana))
                {
                    return BadRequest("Unknown time zone: " + tenant.ScheduleTimeZone);
                }
                tenant.ScheduleTimeZone = iana;
            }
            else
            {
                tenant.ScheduleTimeZone = null;
            }

            await tenantRepo.TenantUpdateAsync(tenant);
            return Ok();
        }

        // ---- Emergency stop (roadmap #230) -----------------------------------

        /// Fail-closed, tenant-wide: forces every actuator in idTenant (defaulting to the caller's own) off ahead of any rule, until explicitly cleared. Deliberately one click, no confirmation - unlike a destructive action, hesitation here is the wrong default.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("EmergencyStop")]
        public async Task<ActionResult> EmergencyStopActivate(int? idTenant = null)
        {
            int targetTenantId = idTenant ?? CallerTenantId ?? -1;
            if (!CallerManagesDevices(targetTenantId))
            {
                return StatusCode(403, "Emergency stop requires managing devices in this tenant.");
            }
            await tenantRepo.TenantEmergencyStopSetAsync(targetTenantId, true);
            await WriteAuditAsync("Tenant.EmergencyStopActivated", targetTenantId, "Tenant", targetTenantId.ToString(), null);
            return Ok();
        }

        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("EmergencyStop/Clear")]
        public async Task<ActionResult> EmergencyStopClear(int? idTenant = null)
        {
            int targetTenantId = idTenant ?? CallerTenantId ?? -1;
            if (!CallerManagesDevices(targetTenantId))
            {
                return StatusCode(403, "Emergency stop requires managing devices in this tenant.");
            }
            await tenantRepo.TenantEmergencyStopSetAsync(targetTenantId, false);
            await WriteAuditAsync("Tenant.EmergencyStopCleared", targetTenantId, "Tenant", targetTenantId.ToString(), null);
            return Ok();
        }

        /// Lets the Web layout show a persistent banner without needing the wider TenantReaders role TenantGet requires.
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpGet("EmergencyStop")]
        public async Task<ActionResult<bool>> EmergencyStopStatus(int? idTenant = null)
        {
            int targetTenantId = idTenant ?? CallerTenantId ?? -1;
            if (!CallerManagesDevices(targetTenantId))
            {
                return StatusCode(403, "Emergency stop requires managing devices in this tenant.");
            }
            Tenant? tenant = await tenantRepo.TenantGetByIdAsync(targetTenantId);
            return Ok(tenant?.EmergencyStopActive ?? false);
        }

        // ---- Export/Import --------------------------------------------------

        /// SENSITIVE: carries every exported user's password hash/salt and device's ApiKey (treat like a credential bundle, never persisted server-side, built in memory and streamed straight back) - a TenantAdmin exports only their OWN tenant, Global admin any. ZIP-packaged (single export.json entry, see TenantExportService.BuildExportZipAsync) - same repackaging #124 already applies to the firmware catalog.
        [Authorize(Roles = RoleNames.Admins)]
        [HttpGet("Export")]
        public async Task<ActionResult> Export(int idTenant, bool includeSensorData = false, DateTime? sensorDataSinceUtc = null, CancellationToken cancellationToken = default)
        {
            if (!CallerIsGlobalAdmin && !(CallerHasRole(RoleNames.TenantAdmin) && CallerTenantId == idTenant))
            {
                return StatusCode(403, "Exporting a tenant requires being its Tenant admin, or Global admin.");
            }
            (Stream content, string fileName) = await exportService.BuildExportZipAsync(idTenant, includeSensorData, sensorDataSinceUtc, cancellationToken);
            await WriteAuditAsync("Tenant.Exported", idTenant, "Tenant", idTenant.ToString(), $"includeSensorData={includeSensorData}");
            return File(content, "application/zip", fileName);
        }

        /// ByName only (see api.Models.TenantImportTarget), Global admin only - unlike Export this can create a brand-new tenant or add into one the caller doesn't administer, same bar as TenantAdd/TenantUpdate.
        [Authorize(Roles = RoleNames.LegacyAdmin)]
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

        /// AsSentinel claims TenantID=0 with this export's users/devices, replacing the unclaimed bootstrap admin placeholder (see TenantImportService.ImportAsSentinelAsync, ITenantRepository.TenantZeroIsEmptyAsync) - deliberately anonymous since a brand-new self-hosted server has nobody to authenticate as yet, TenantZeroIsEmptyAsync itself blocks this against an already-provisioned server.
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
