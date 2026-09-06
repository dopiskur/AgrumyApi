using System.IO.Compression;
using System.Text.Json;
using api.Dal.Interface;
using api.Models;

namespace api.Migration
{
    /// Builds the full portable snapshot of one tenant - see api.Models.TenantExport for exactly what is/isn't included and why; read-only, composed from existing IRepository reads.
    public class TenantExportService(IRepository repo)
    {
        // Human-readable (WriteIndented) - same convention as DeviceFarmUnitZoneRule.ConditionConfig - an admin may open this JSON to sanity-check it before importing elsewhere.
        private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        /// Packages ExportAsync's snapshot into a ZIP (single export.json entry) - same repackaging #124 already applies to the firmware catalog, so a tenant export behaves like every other admin download/upload pair instead of being the one plain-JSON exception.
        public async Task<(Stream Content, string FileName)> BuildExportZipAsync(int tenantId, bool includeSensorData, DateTime? sensorDataSinceUtc, CancellationToken cancellationToken = default)
        {
            TenantExport export = await ExportAsync(tenantId, includeSensorData, sensorDataSinceUtc);

            var zipStream = new MemoryStream();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using Stream entry = zip.CreateEntry(TenantExport.ExportEntryName, CompressionLevel.Optimal).Open();
                await JsonSerializer.SerializeAsync(entry, export, ExportJsonOptions, cancellationToken);
            }
            zipStream.Position = 0;

            string tenantSlug = (export.SourceTenantName ?? "export").ToLowerInvariant().Replace(' ', '-');
            string fileName = $"agrumy-tenant-{tenantSlug}-{DateTime.UtcNow:yyyyMMdd}.zip";
            return (zipStream, fileName);
        }

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

            IList<DeviceFarmUnit> units = await repo.DeviceFarmUnitsGetAsync(tenantId);
            var zones = new List<DeviceFarmUnitZone>();
            var rules = new List<DeviceFarmUnitZoneRule>();
            foreach (DeviceFarmUnit unit in units)
            {
                if (unit.IDDeviceFarmUnit is not int idUnit)
                {
                    continue;
                }
                IList<DeviceFarmUnitZone> unitZones = await repo.DeviceFarmUnitZonesGetAsync(idUnit);
                zones.AddRange(unitZones);
                foreach (DeviceFarmUnitZone zone in unitZones)
                {
                    if (zone.IDDeviceFarmUnitZone is int idZone)
                    {
                        rules.AddRange(await repo.RulesGetForZoneAsync(idZone));
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
