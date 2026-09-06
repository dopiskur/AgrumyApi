using api.Dal.Interface;
using api.Models;

namespace api.Dal
{
    /// IRefreshTokenRepository members - forwarded to the standalone EfRefreshTokenRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<int> RefreshTokenAddAsync(int userID, string tokenHash, DateTime expiresAt) =>
            refreshTokenRepository.RefreshTokenAddAsync(userID, tokenHash, expiresAt);

        public Task<RefreshTokenInfo?> RefreshTokenGetAsync(string tokenHash) => refreshTokenRepository.RefreshTokenGetAsync(tokenHash);

        public Task<bool> RefreshTokenRotateAsync(int userId, string oldTokenHash, string newTokenHash, DateTime newExpiresAt) =>
            refreshTokenRepository.RefreshTokenRotateAsync(userId, oldTokenHash, newTokenHash, newExpiresAt);

        public Task RefreshTokenRevokeAsync(string tokenHash) => refreshTokenRepository.RefreshTokenRevokeAsync(tokenHash);

        public Task RefreshTokenRevokeAllForUserAsync(int userID) => refreshTokenRepository.RefreshTokenRevokeAllForUserAsync(userID);
    }
}
