using api.Dal.Entities;
using api.Models;
using api.Security;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IUserRepository members: bootstrap admin - the "is there a pending first-run admin" check and its one-time password set.
    internal partial class EfRepository
    {
        public async Task<bool> BootstrapAdminPendingAsync()
        {
            return await db.Users.AsNoTracking().AnyAsync(u => u.PwdHash == null);
        }

        /// Fetch-then-verify-then-write (verification needs C#), but still race-safe since the final write stays gated by WHERE PwdHash IS NULL.
        public async Task<bool> BootstrapAdminSetPasswordAsync(UserSecret secret, string setupSecret)
        {
            UserRow? pending = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PwdHash == null);
            if (pending == null || !AuthenticationProvider.VerifyHash(pending.BootstrapSecretHash, pending.BootstrapSecretSalt, setupSecret))
            {
                return false;
            }

            // WHERE PwdHash IS NULL (not a Login/email match) is what makes the door close permanently once used - clearing BootstrapSecret* isn't load-bearing for replay but leaves no live secret hash lying around.
            int rows = await db.Users.Where(u => u.PwdHash == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PwdHash, secret.PwdHash)
                    .SetProperty(u => u.PwdSalt, secret.PwdSalt)
                    .SetProperty(u => u.BootstrapSecretHash, (string?)null)
                    .SetProperty(u => u.BootstrapSecretSalt, (string?)null));
            return rows > 0;
        }

        public async Task<bool> BootstrapAdminDiscardPendingAsync()
        {
            int rows = await db.Users.Where(u => u.PwdHash == null).ExecuteDeleteAsync();
            return rows > 0;
        }
    }
}
