using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>ITenantRepository members.</summary>
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
    }
}
