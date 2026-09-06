using api.Models;

namespace api.Dal
{
    /// IUserRepository activation members - forwarded to the standalone EfUserRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task UserSetActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt) =>
            userRepository.UserSetActivationTokenAsync(idUser, tokenHash, expiresAt);

        public Task<bool> UserIssueActivationTokenAsync(int idUser, string tokenHash, DateTime expiresAt, int cooldownMinutes) =>
            userRepository.UserIssueActivationTokenAsync(idUser, tokenHash, expiresAt, cooldownMinutes);

        public Task<User?> UserActivateAsync(string tokenHash) => userRepository.UserActivateAsync(tokenHash);
    }
}
