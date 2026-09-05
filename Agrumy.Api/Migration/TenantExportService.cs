using api.Dal.Interface;
using api.Models;

namespace api.Migration
{
    /// Builds the full portable snapshot of one tenant - see api.Models.TenantExport for exactly what is/isn't included and why; read-only, composed from existing IRepository reads.
    public class TenantExportService(IRepository repo)
    {
        public async Task<TenantExport> ExportAsync(int tenantId, bool includeSensorData, DateTime? sensorDataSinceUtc)
        {
            Tenant? tenant = await repo.TenantGetByIdAsync(tenantId);

            var exportUsers = new List<TenantExportUser>();
            foreach (User u in await repo.UsersGetAsync(tenantId))
            {
                if (u.IDUser is not int idUser)
                {
                    continue;
                }
                UserSecret? secret = await repo.UserSecretGetAsync(idUser, null, null);
                IReadOnlyList<string> roles = await repo.UserRoleNamesGetAsync(idUser);
                exportUsers.Add(new TenantExportUser
                {
                    User = u,
                    PwdHash = secret?.PwdHash,
                    PwdSalt = secret?.PwdSalt,
                    Roles = roles.ToList(),
                });
            }

            IList<DeviceUnit> units = await repo.DeviceUnitsGetAsync(tenantId);
            var zones = new List<DeviceUnitZone>();
            var rules = new List<DeviceUnitZoneRule>();
            foreach (DeviceUnit unit in units)
            {
                if (unit.IDDeviceUnit is not int idUnit)
                {
                    continue;
                }
                IList<DeviceUnitZone> unitZones = await repo.DeviceUnitZonesGetAsync(idUnit);
                zones.AddRange(unitZones);
                foreach (DeviceUnitZone zone in unitZones)
                {
                    if (zone.IDDeviceUnitZone is int idZone)
                    {
                        rules.AddRange(await repo.DeviceUnitZoneRulesGetAsync(idZone));
                    }
                }
            }

            var exportDevices = new List<TenantExportDevice>();
            foreach (Device d in await repo.DevicesGetAsync(tenantId))
            {
                exportDevices.Add(new TenantExportDevice
                {
                    Device = d,
                    // Read off the in-memory Device, not left to JSON serialization - see TenantExportDevice's remarks for why that would drop them.
                    ApiId = d.ApiId,
                    ApiKey = d.ApiKey,
                    Sensor = d.DeviceConfigSensorID is int sId ? await repo.DeviceConfigSensorGetAsync(sId) : null,
                    Controller = d.DeviceConfigControllerID is int cId ? await repo.DeviceConfigControllerGetAsync(cId) : null,
                });
            }

            return new TenantExport
            {
                ExportedAtUtc = DateTime.UtcNow,
                SourceTenantName = tenant?.TenantName,
                Users = exportUsers,
                Units = units,
                Zones = zones,
                ZoneRules = rules,
                Devices = exportDevices,
                IncludesSensorData = includeSensorData,
                SensorData = includeSensorData ? await repo.SensorDataExportGetAsync(tenantId, sensorDataSinceUtc) : null,
            };
        }
    }
}
