using System.Text.Json;
using api.Commands;
using api.Dal.Interface;
using api.Models;
using api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace api.Controllers.API
{
    /// "Scan for new devices" - device-facing report intake, the admin scan trigger, the aggregated results list, and Register (PIN + WiFi credentials to the winning scanning device).
    [Route("/api/Discovery")]
    public class DiscoveryApiController(IRepository repo, ICache cache, CommandQueueService commandQueue) : ApiControllerBase(repo, cache)
    {
        /// No identity field in the body by design - the scanning device comes exclusively from the authenticated apiId, same rule as DeviceApiController.PushEvent.
        [HttpPost("Report")]
        [EnableRateLimiting("device-data")]
        [Authorize(Policy = DeviceAuth.SessionPolicy)]
        public async Task<ActionResult> Report([FromBody] DiscoveryReportRequest value)
        {
            if (string.IsNullOrWhiteSpace(value.DiscoveredApMac))
            {
                return BadRequest("discoveredApMac is required.");
            }

            string apiId = HttpContext.DeviceApiId()!;
            Device? device = await Repo.DeviceGetByApiIdAsync(apiId);
            if (device is null)
            {
                return Unauthorized();
            }

            await Repo.DiscoveryReportAddAsync(device.IDDevice!.Value, value.DiscoveredApMac, value.Rssi);
            return Ok();
        }

        /// Fans ScanForDevices out to every sensor-only device in scope (see CommandQueueService.IssueScanCommandAsync for Zone/Unit/Fleet-wide resolution) - Zone/Unit ownership is checked like DeviceCommandApiController's targets, Fleet-wide scopes by the caller's own tenant instead (null only for a global device manager).
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Scan")]
        public async Task<ActionResult<IReadOnlyList<int>>> Scan([FromBody] DiscoveryScanRequest request)
        {
            if (request.ZoneID is int zoneId)
            {
                var (_, error) = await EnsureOwnedZoneAsync(zoneId, forWrite: true);
                if (error != null)
                {
                    return error;
                }
            }
            else if (request.UnitID is int unitId)
            {
                var (_, error) = await EnsureOwnedUnitAsync(unitId, forWrite: true);
                if (error != null)
                {
                    return error;
                }
            }

            int? tenantId = CallerManagesDevicesGlobally ? null : CallerTenantId;
            IssueCommandResult result = await commandQueue.IssueScanCommandAsync(tenantId, request.UnitID, request.ZoneID);
            return result.Outcome switch
            {
                IssueCommandOutcome.Success => Ok(result.CreatedCommandIds),
                IssueCommandOutcome.AllDuplicates => Conflict(result.Message),
                IssueCommandOutcome.TargetNotFound => NotFound(result.Message),
                _ => StatusCode(500),
            };
        }

        /// Lets the Web layer decide the Register modal's shape (free-text SSID/password, a dropdown, or nothing) before the admin ever submits - Password is always stripped, same as Register's WifiChoices. Open to any authenticated caller, same rule as DeviceApiController.DeviceFleetGet - the Register modal itself is still gated to DeviceManagers in the Web UI.
        [Authorize]
        [HttpGet("WifiConfigs")]
        public async Task<ActionResult<IList<TenantWifiConfig>>> WifiConfigs()
        {
            if (CallerTenantId is not int tenantId)
            {
                return Ok(new List<TenantWifiConfig>());
            }
            IList<TenantWifiConfig> configs = await Repo.TenantWifiConfigsGetAsync(tenantId);
            return Ok(configs.Select(c => new TenantWifiConfig { IDTenantWifiConfig = c.IDTenantWifiConfig, TenantID = c.TenantID, Ssid = c.Ssid }).ToList());
        }

        /// Open to any authenticated caller, same rule as DeviceApiController.DeviceFleetGet - the Register modal that acts on these results is still gated to DeviceManagers in the Web UI.
        [Authorize]
        [HttpGet("Results")]
        public async Task<ActionResult<IList<DiscoveryResult>>> Results(int? unitID, int? zoneID)
        {
            if (zoneID is int zoneId)
            {
                var (_, error) = await EnsureOwnedZoneAsync(zoneId, forWrite: false);
                if (error != null)
                {
                    return error;
                }
            }
            else if (unitID is int unitId)
            {
                var (_, error) = await EnsureOwnedUnitAsync(unitId, forWrite: false);
                if (error != null)
                {
                    return error;
                }
            }

            int? tenantId = CallerReadsDevicesGlobally ? null : CallerTenantId;
            return Ok(await Repo.DiscoveryResultsGetAsync(tenantId, unitID, zoneID));
        }

        /// <summary>Resolves the winning scanning device for DiscoveredApMac, resolves WiFi
        /// credentials (0/1/many saved TenantWifiConfig rows - see api.Models.DiscoveryRegisterRequest),
        /// (re)issues the caller's own #70 device-PIN, and queues a ProvisionDevice command carrying
        /// both to that device. DeviceName/UnitID/ZoneID are accepted and stored on the command's
        /// payload but nothing applies them yet - no later step in this roadmap item's plan closes
        /// that loop.</summary>
        [Authorize(Roles = RoleNames.DeviceManagers)]
        [HttpPost("Register")]
        public async Task<ActionResult<DiscoveryRegisterResult>> Register([FromBody] DiscoveryRegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DiscoveredApMac))
            {
                return BadRequest("discoveredApMac is required.");
            }

            if (request.ZoneID is int zoneId)
            {
                var (_, zoneError) = await EnsureOwnedZoneAsync(zoneId, forWrite: true);
                if (zoneError != null)
                {
                    return zoneError;
                }
            }
            else if (request.UnitID is int unitId)
            {
                var (_, unitError) = await EnsureOwnedUnitAsync(unitId, forWrite: true);
                if (unitError != null)
                {
                    return unitError;
                }
            }

            int? callerTenantId = CallerManagesDevicesGlobally ? null : CallerTenantId;
            DiscoveryResult? winner = await Repo.DiscoveryResultGetAsync(request.DiscoveredApMac, callerTenantId);
            if (winner is null)
            {
                return NotFound($"No scan report found for {request.DiscoveredApMac}.");
            }

            string ssid, wifiPassword;
            IList<TenantWifiConfig> wifiConfigs = await Repo.TenantWifiConfigsGetAsync(winner.TenantID);
            if (wifiConfigs.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(request.Ssid) || string.IsNullOrWhiteSpace(request.WifiPassword))
                {
                    return Ok(new DiscoveryRegisterResult { Outcome = DiscoveryRegisterOutcome.WifiCredentialsRequired });
                }
                ssid = request.Ssid;
                wifiPassword = request.WifiPassword;
                if (request.SaveWifiForLater)
                {
                    await Repo.TenantWifiConfigAddAsync(new TenantWifiConfig { TenantID = winner.TenantID, Ssid = ssid, Password = wifiPassword });
                }
            }
            else if (wifiConfigs.Count == 1)
            {
                ssid = wifiConfigs[0].Ssid;
                wifiPassword = wifiConfigs[0].Password!;
            }
            else if (wifiConfigs.FirstOrDefault(c => c.IDTenantWifiConfig == request.WifiConfigId) is TenantWifiConfig chosen)
            {
                ssid = chosen.Ssid;
                wifiPassword = chosen.Password!;
            }
            else
            {
                return Ok(new DiscoveryRegisterResult
                {
                    Outcome = DiscoveryRegisterOutcome.WifiConfigChoiceRequired,
                    WifiChoices = wifiConfigs.Select(c => new TenantWifiConfig { IDTenantWifiConfig = c.IDTenantWifiConfig, TenantID = c.TenantID, Ssid = c.Ssid }).ToList(),
                });
            }

            string? callerName = User.Identity?.Name;
            if (string.IsNullOrEmpty(callerName))
            {
                return Unauthorized();
            }
            User? caller = await Repo.UserGetAsync(null, callerName, null);
            if (caller?.IDUser is not int callerUserId)
            {
                return NotFound();
            }

            string pin = AuthenticationProvider.GetPin();
            DateTime pinExpiresAt = DateTime.UtcNow.AddHours(AuthenticationProvider.PinValidHours);
            await Repo.UserSetDevicePinAsync(callerUserId, pin, pinExpiresAt);

            string payloadJson = JsonSerializer.Serialize(new DiscoveryProvisionPayload
            {
                Username = callerName,
                Pin = pin,
                DiscoveredApMac = request.DiscoveredApMac,
                Ssid = ssid,
                WifiPassword = wifiPassword,
                DeviceName = request.DeviceName,
                UnitID = request.UnitID,
                ZoneID = request.ZoneID,
            });

            IssueCommandResult result = await commandQueue.IssueProvisionCommandAsync(winner.ScanningDeviceID, payloadJson);
            return result.Outcome switch
            {
                IssueCommandOutcome.Success => Ok(new DiscoveryRegisterResult { Outcome = DiscoveryRegisterOutcome.Success }),
                IssueCommandOutcome.AllDuplicates => Ok(new DiscoveryRegisterResult { Outcome = DiscoveryRegisterOutcome.AlreadyPending }),
                _ => StatusCode(500),
            };
        }

        private Task<(DeviceUnitZone? Zone, ActionResult? Error)> EnsureOwnedZoneAsync(int idDeviceUnitZone, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(() => Repo.DeviceUnitZoneGetByIdAsync(idDeviceUnitZone), z => z.TenantID, "Zone", forWrite);

        private Task<(DeviceUnit? Unit, ActionResult? Error)> EnsureOwnedUnitAsync(int idDeviceUnit, bool forWrite) =>
            EnsureOwnedDeviceEntityAsync(() => Repo.DeviceUnitGetByIdAsync(idDeviceUnit), u => u.TenantID, "Unit", forWrite);
    }
}
