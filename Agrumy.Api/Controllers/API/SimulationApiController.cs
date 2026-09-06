using api.Dal.Interface;
using api.Models;
using api.Security;
using api.Simulation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers.API
{
    /// Admin-facing create/list/delete for fully virtual devices - the actual per-tick simulation runs in api.BackgroundWorkers.VirtualDeviceRunnerBackgroundService, not here.
    [Route("/api/Simulation")]
    public class SimulationApiController(ISimulationRepository simulationRepo, IDeviceRepository deviceRepo, IUserRepository userRepo, IAuditLogRepository auditLogRepo, ICache cache, IHttpClientFactory httpClientFactory) : ApiControllerBase(userRepo, auditLogRepo, cache)
    {
        // Separate field, not the primary-constructor parameter directly - a parameter used both here and in the base(...) call trips CS9107 (ambiguous double-capture).
        private readonly IUserRepository users = userRepo;
        /// Creates the device via the SAME POST /api/Device/Register a real device calls after WiFi setup (Option C design - the endpoint never learns the caller isn't real hardware), then tags it in the virtual-device registry so the background runner picks it up. Deliberately bare - no Name/Unit/Zone here, an admin configures those afterward through the ordinary Device Edit/Fleet UI, same as any freshly-registered real device.
        [Authorize(Roles = RoleNames.SimulationManagers)]
        [HttpPost("Device")]
        public async Task<ActionResult<DeviceDto>> CreateVirtualDevice()
        {
            string? callerName = User.Identity?.Name;
            if (string.IsNullOrEmpty(callerName))
            {
                return Unauthorized();
            }
            User? caller = await users.UserGetAsync(null, callerName, null);
            if (caller?.IDUser is not int callerUserId)
            {
                return NotFound();
            }

            string pin = AuthenticationProvider.GetPin();
            await users.UserSetDevicePinAsync(callerUserId, pin, DateTime.UtcNow.AddHours(AuthenticationProvider.PinValidHours));

            // "02" is a locally-administered MAC prefix (IEEE 802-2014 sec 8.2.2) - guaranteed to never collide with a real vendor OUI a physical device might report.
            string mac = "02" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

            var client = new VirtualDeviceClient(httpClientFactory.CreateClient(BackgroundWorkers.VirtualDeviceRunnerBackgroundService.HttpClientName));
            DeviceConfig? config = await client.RegisterAsync(mac, callerName, pin, displayName: null);
            if (config?.deviceID is not int deviceId)
            {
                return StatusCode(502, "Virtual device registration did not return a device id.");
            }

            await simulationRepo.VirtualDeviceRegisterAsync(deviceId);
            // ApiId/ApiKey stay internal - same rule as every other device-facing GET, never returned to an admin caller (DeviceDto has no property for either).
            Device? created = await deviceRepo.DeviceGetByIdAsync(deviceId);
            return Ok(created?.ToDto());
        }

        /// Tenant-scoped for everyone including Global admin - a deliberate deviation from the usual Global-admin-sees-everything pattern, since a simulation is scoped to the tenant it was created for.
        [Authorize(Roles = RoleNames.SimulationManagers)]
        [HttpGet("Device")]
        public async Task<ActionResult<IList<int>>> ListVirtualDevices() =>
            Ok(await simulationRepo.VirtualDeviceIdsGetAsync(CallerTenantId));

        [Authorize(Roles = RoleNames.SimulationManagers)]
        [HttpDelete("Device/{idDevice}")]
        public async Task<ActionResult> DeleteVirtualDevice(int idDevice)
        {
            Device? device = await deviceRepo.DeviceGetByIdAsync(idDevice);
            if (device is null)
            {
                return NotFound();
            }
            if (!CallerManagesDevices(device.TenantID))
            {
                return StatusCode(403, "Device belongs to a different tenant");
            }

            await simulationRepo.VirtualDeviceDeleteAsync(idDevice, device.TenantID);
            return Ok();
        }
    }
}
