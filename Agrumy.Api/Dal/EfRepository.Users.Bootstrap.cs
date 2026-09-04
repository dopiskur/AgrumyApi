using api.Dal.Entities;
using api.Models;
using api.Security;
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

        /// <summary>Fetch-then-verify-then-write (verification needs C#), but still race-safe since the final write stays gated by WHERE PwdHash IS NULL.</summary>
        public async Task<bool> BootstrapAdminSetPasswordAsync(UserSecret secret, string setupSecret)
        {
            UserRow? pending = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PwdHash == null);
            if (pending == null || !AuthenticationProvider.VerifyHash(pending.BootstrapSecretHash, pending.BootstrapSecretSalt, setupSecret))
            {
                return false;
            }

            // WHERE PwdHash IS NULL, not a Login/email match - this is what makes the door close
            // permanently once used. Clearing the BootstrapSecret* columns here isn't load-bearing
            // for replay (PwdHash IS NULL already stops that) but leaves no live secret hash sitting
            // around once it has served its purpose.
            int rows = await db.Users.Where(u => u.PwdHash == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.PwdHash, secret.PwdHash)
                    .SetProperty(u => u.PwdSalt, secret.PwdSalt)
                    .SetProperty(u => u.BootstrapSecretHash, (string?)null)
                    .SetProperty(u => u.BootstrapSecretSalt, (string?)null));
            return rows > 0;
        }
    }
}
