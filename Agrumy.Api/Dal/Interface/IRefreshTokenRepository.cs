using api.Models;

namespace api.Dal.Interface
{
    /// Refresh-token facet - tokens are opaque, single-use, rotated on every redemption; only a SHA-256 hash ever reaches the DB or these signatures.
    public interface IRefreshTokenRepository
    {
        Task<int> RefreshTokenAddAsync(int userID, string tokenHash, DateTime expiresAt);

        /// The token identified by its hash, or null if no such token was ever issued.
        Task<RefreshTokenInfo?> RefreshTokenGetAsync(string tokenHash);

        /// Revokes oldTokenHash via one atomic WHERE RevokedAt IS NULL update; returns false if that affected zero rows (already revoked/missing/lost the race).
        Task<bool> RefreshTokenRotateAsync(int userId, string oldTokenHash, string newTokenHash, DateTime newExpiresAt);

        /// Revokes one token (explicit logout) - idempotent, revoking an unknown or already-revoked token is not an error.
        Task RefreshTokenRevokeAsync(string tokenHash);

        /// Revokes every active token for a user - the response to detecting reuse of an already-rotated token, which signals theft, so every session dies, not just the one caught.
        Task RefreshTokenRevokeAllForUserAsync(int userID);
    }
}
