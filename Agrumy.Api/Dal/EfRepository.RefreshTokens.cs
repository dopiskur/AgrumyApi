using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IRefreshTokenRepository members.</summary>
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

        public async Task<bool> RefreshTokenRotateAsync(int userId, string oldTokenHash, string newTokenHash, DateTime newExpiresAt)
        {
            // Same atomic WHERE RevokedAt == null guard as RefreshTokenRevokeAsync (roadmap #181) -
            // two concurrent redemptions of the same token race for this single UPDATE statement,
            // and only one can affect a row; the other gets 0 rows back and must not insert a
            // second new token for a rotation it lost.
            int rows = await db.RefreshTokens.Where(t => t.TokenHash == oldTokenHash && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, DateTime.UtcNow)
                    .SetProperty(t => t.ReplacedByTokenHash, newTokenHash));
            if (rows == 0)
            {
                return false;
            }

            db.RefreshTokens.Add(new RefreshTokenRow
            {
                UserID = userId,
                TokenHash = newTokenHash,
                ExpiresAt = newExpiresAt,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return true;
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
