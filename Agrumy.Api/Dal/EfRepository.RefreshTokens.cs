using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IRefreshTokenRepository members (roadmap #74 split).</summary>
    internal partial class EfRepository
    {
        public async Task<int> RefreshTokenAddAsync(int userID, string tokenHash, DateTime expiresAt)
        {
            var row = new RefreshTokenRow
            {
                UserID = userID,
                TokenHash = tokenHash,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow,
            };
            db.RefreshTokens.Add(row);
            await db.SaveChangesAsync();
            return row.IDRefreshToken;
        }

        public async Task<RefreshTokenInfo?> RefreshTokenGetAsync(string tokenHash)
        {
            var row = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
            return row == null
                ? null
                : new RefreshTokenInfo { UserID = row.UserID, ExpiresAt = row.ExpiresAt, RevokedAt = row.RevokedAt };
        }

        public async Task RefreshTokenRotateAsync(string oldTokenHash, string newTokenHash, DateTime newExpiresAt)
        {
            var old = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == oldTokenHash);
            if (old == null || old.RevokedAt != null)
            {
                return; // caller already checked expiry/reuse; nothing valid left to rotate
            }

            old.RevokedAt = DateTime.UtcNow;
            old.ReplacedByTokenHash = newTokenHash;
            db.RefreshTokens.Add(new RefreshTokenRow
            {
                UserID = old.UserID,
                TokenHash = newTokenHash,
                ExpiresAt = newExpiresAt,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(); // one transaction: revoke old + insert new
        }

        public async Task RefreshTokenRevokeAsync(string tokenHash)
        {
            await db.RefreshTokens.Where(t => t.TokenHash == tokenHash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        }

        public async Task RefreshTokenRevokeAllForUserAsync(int userID)
        {
            await db.RefreshTokens.Where(t => t.UserID == userID && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow));
        }
    }
}
