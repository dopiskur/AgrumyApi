using api.Models;

namespace api.Dal
{
    /// IUserRepository bootstrap-admin members - forwarded to the standalone EfUserRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<bool> BootstrapAdminPendingAsync() => userRepository.BootstrapAdminPendingAsync();

        public Task<bool> BootstrapAdminSetPasswordAsync(UserSecret secret, string setupSecret) => userRepository.BootstrapAdminSetPasswordAsync(secret, setupSecret);

        public Task<bool> BootstrapAdminDiscardPendingAsync() => userRepository.BootstrapAdminDiscardPendingAsync();
    }
}
