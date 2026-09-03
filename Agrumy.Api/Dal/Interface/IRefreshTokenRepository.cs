using api.Models;

namespace api.Dal.Interface
{
    /// <summary>Refresh-token facet. Tokens are opaque, single-use, rotated on every redemption.
    /// Only a SHA-256 hash of the token ever reaches the DB or these method signatures.</summary>
    public interface IRefreshTokenRepository
    {
        Task<int> RefreshTokenAddAsync(int userID, string tokenHash, DateTime expiresAt);

        /// <summary>The token identified by its hash, or null if no such token was ever issued.</summary>
        Task<RefreshTokenInfo?> RefreshTokenGetAsync(string tokenHash);

        /// <summary>Atomically revokes <paramref name="oldTokenHash"/> (pointing it at the new hash,
        /// for reuse-chain tracking) and inserts the new token row. No-op if the old token is missing
        /// or already revoked - the caller is expected to have checked that first.</summary>
        Task RefreshTokenRotateAsync(string oldTokenHash, string newTokenHash, DateTime newExpiresAt);

        /// <summary>Revokes one token (explicit logout). Idempotent - revoking an unknown or
        /// already-revoked token is not an error.</summary>
        Task RefreshTokenRevokeAsync(string tokenHash);

        /// <summary>Revokes every active token for a user - the response to detecting reuse of an
        /// already-rotated token (signals the token was stolen, so every session dies, not just the
        /// one that got caught).</summary>
        Task RefreshTokenRevokeAllForUserAsync(int userID);
    }
}
