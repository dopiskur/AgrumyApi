using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json.Nodes;

namespace api.Controllers.API
{
    [Route("/api/SensorData")]
    public class SensorDataController(IRepository repo, ICache cache) : ApiControllerBase(repo, cache)
    {
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<string>> Get(int? deviceID, int? timeRange = 60, int? timeMDMY = 0, int? buildReport = 0) =>
            // Roadmap #183: was always tenant-scoped even for a Global reader/admin, unlike every
            // other Device-domain list (DevicesGet, DeviceFleetGet, DeviceUnit dashboards).
            Ok(await Repo.SensorDataGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, deviceID, timeRange, timeMDMY, buildReport));

        [HttpPost]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult<int?>> Post([FromBody] JsonArray jsonArray)
        {
            string apiId = HttpContext.DeviceApiId()!;

            // Device/tenant come from the authenticated identity, never from the payload.
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await Repo.SensorDataPushAsync(jsonArray, device.IDDevice!.Value, device.TenantID,
                device.DeviceUnitID, device.DeviceUnitZoneID);

            return Ok(device.ConfigVersion);
        }

        /// <summary>Deleting telemetry is device management, gated to the device-manager roles; the target device's own tenant is resolved and checked explicitly.</summary>
        [HttpDelete]
        [Authorize(Roles = RoleNames.DeviceManagers)]
        public async Task<ActionResult> Delete(int deviceID, int timeMDMY = 0, int timeRange = 0)
        {
            Device? device = await Repo.DeviceGetByIdAsync(deviceID);
            if (device is null)
            {
                return NotFound();
            }
            if (!CallerManagesDevices(device.TenantID))
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            await Repo.SensorDataDeleteAsync(device.TenantID, deviceID, timeRange, timeMDMY);
            return Ok();
        }

        [HttpGet("Report")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<SensorDataReport>>> ReportGet(int? getData, int? idDevice, int? iDSensorDataReport) =>
            // Roadmap #183: same fix as Get above.
            Ok(await Repo.SensorDataReportGetAsync(CallerReadsDevicesGlobally ? null : CallerTenantId, getData, idDevice, iDSensorDataReport));
    }
}
