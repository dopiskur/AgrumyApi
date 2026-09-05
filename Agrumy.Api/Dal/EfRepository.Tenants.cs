using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// ITenantRepository members.
    internal partial class EfRepository
    {
        public async Task<bool> TenantGetAsync(string tenantName)
        {
            return await db.Tenants.AsNoTracking().AnyAsync(t => t.TenantName == tenantName);
        }

        public async Task<int?> TenantGetIdAsync(string tenantName)
        {
            return await db.Tenants.AsNoTracking()
                .Where(t => t.TenantName == tenantName)
                .Select(t => (int?)t.IDTenant)
                .FirstOrDefaultAsync();
        }

        public async Task<int> TenantAddAsync(string tenantName)
        {
            var row = new TenantRow { TenantName = tenantName };
            db.Tenants.Add(row);
            await db.SaveChangesAsync();
            return row.IDTenant;
        }

        public async Task<IList<Tenant>> TenantsGetAllAsync()
        {
            return await db.Tenants.AsNoTracking()
                .OrderBy(t => t.TenantName)
                .Select(t => new Tenant { IDTenant = t.IDTenant, TenantName = t.TenantName })
                .ToListAsync();
        }

        public async Task<Tenant?> TenantGetByIdAsync(int idTenant)
        {
            var row = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.IDTenant == idTenant);
            return row == null ? null : new Tenant { IDTenant = row.IDTenant, TenantName = row.TenantName };
        }

        public async Task TenantUpdateAsync(Tenant tenant)
        {
            var row = await db.Tenants.FirstOrDefaultAsync(t => t.IDTenant == tenant.IDTenant);
            if (row == null)
            {
                return;
            }
            row.TenantName = tenant.TenantName ?? row.TenantName;
            await db.SaveChangesAsync();
        }

        public async Task<bool> TenantZeroIsEmptyAsync()
        {
            if (await db.Devices.AsNoTracking().AnyAsync(d => d.TenantID == 0))
            {
                return false;
            }
            var tenant0Users = await db.Users.AsNoTracking().Where(u => u.TenantID == 0)
                .Select(u => u.PwdHash).ToListAsync();
            return tenant0Users.Count == 0 || (tenant0Users.Count == 1 && tenant0Users[0] == null);
        }

        public async Task<IList<TenantWifiConfig>> TenantWifiConfigsGetAsync(int tenantID)
        {
            return await db.TenantWifiConfigs.AsNoTracking()
                .Where(c => c.TenantID == tenantID)
                .Select(c => new TenantWifiConfig
                {
                    IDTenantWifiConfig = c.IDTenantWifiConfig,
                    TenantID = c.TenantID,
                    Ssid = c.Ssid,
                    Password = c.Password,
                })
                .ToListAsync();
        }

        public async Task<TenantWifiConfig> TenantWifiConfigAddAsync(TenantWifiConfig config)
        {
            var row = new TenantWifiConfigRow { TenantID = config.TenantID, Ssid = config.Ssid, Password = config.Password ?? "" };
            db.TenantWifiConfigs.Add(row);
            await db.SaveChangesAsync();
            return new TenantWifiConfig { IDTenantWifiConfig = row.IDTenantWifiConfig, TenantID = row.TenantID, Ssid = row.Ssid, Password = row.Password };
        }

        public async Task<TenantWifiConfig?> TenantWifiConfigGetByIdAsync(int idTenantWifiConfig)
        {
            var row = await db.TenantWifiConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.IDTenantWifiConfig == idTenantWifiConfig);
            return row == null ? null : new TenantWifiConfig { IDTenantWifiConfig = row.IDTenantWifiConfig, TenantID = row.TenantID, Ssid = row.Ssid, Password = row.Password };
        }

        public async Task TenantWifiConfigUpdateAsync(TenantWifiConfig config)
        {
            var row = await db.TenantWifiConfigs.FirstOrDefaultAsync(c => c.IDTenantWifiConfig == config.IDTenantWifiConfig);
            if (row == null)
            {
                return;
            }
            row.Ssid = config.Ssid;
            row.Password = config.Password ?? "";
            await db.SaveChangesAsync();
        }

        public async Task TenantWifiConfigDeleteAsync(int idTenantWifiConfig)
        {
            await db.TenantWifiConfigs.Where(c => c.IDTenantWifiConfig == idTenantWifiConfig).ExecuteDeleteAsync();
        }
    }
}
