using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IUserRepository members: email activation - issuing/reissuing an activation token and redeeming it.
    internal partial class EfRepository
    {

        public async Task UserSetActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt)
        {
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == idUser);
            if (row is null) { return; }

            row.ActivationTokenHash = tokenHash;
            row.ActivationTokenExpiresAt = expiresAt;
            row.ActivationLastSentAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        public async Task<bool> UserIssueActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt, int cooldownMinutes)
        {
            var row = await db.Users.FirstOrDefaultAsync(u => u.IDUser == idUser);
            if (row is null || row.EmailVerified)
            {
                return false;
            }
            if (row.ActivationLastSentAt is DateTime lastSent && lastSent > DateTime.UtcNow.AddMinutes(-cooldownMinutes))
            {
                return false; // still in cooldown
            }

            row.ActivationTokenHash = tokenHash;
            row.ActivationTokenExpiresAt = expiresAt;
            row.ActivationLastSentAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<User?> UserActivateAsync(string tokenHash)
        {
            var row = await db.Users.FirstOrDefaultAsync(u => u.ActivationTokenHash == tokenHash);
            if (row is null || row.ActivationTokenExpiresAt is null || row.ActivationTokenExpiresAt < DateTime.UtcNow)
            {
                return null;
            }

            row.EmailVerified = true;
            row.ActivationTokenHash = null;
            row.ActivationTokenExpiresAt = null;
            await db.SaveChangesAsync();

            return ToDto(row);
        }
    }
}
