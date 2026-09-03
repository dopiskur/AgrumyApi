using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IUserRepository members: bootstrap admin - the "is there a pending first-run admin"
    /// check and its one-time password set.</summary>
    internal partial class EfRepository
    {
        public async Task<bool> BootstrapAdminPendingAsync()
        {
            return await db.Users.AsNoTracking().AnyAsync(u => u.PwdHash == null);
        }

        public async Task<bool> BootstrapAdminSetPasswordAsync(UserSecret secret)
        {
            // WHERE PwdHash IS NULL, not a Login/email match - this is what makes the door close
            // permanently once used.
            int rows = await db.Users.Where(u => u.PwdHash == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PwdHash, secret.PwdHash)
                    .SetProperty(u => u.PwdSalt, secret.PwdSalt));
            return rows > 0;
        }
    }
}
