using api.Models;

namespace api.Dal
{
    /// IUserRepository core members - forwarded to the standalone EfUserRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task UserAddAsync(User user, UserSecret userSecret) => userRepository.UserAddAsync(user, userSecret);

        public Task<int> RegisterUserAsync(User user, UserSecret userSecret, int? existingTenantId, string? newTenantName,
            string activationTokenHash, DateTime activationTokenExpiresAtUtc, IEnumerable<string> startingRoles) =>
            userRepository.RegisterUserAsync(user, userSecret, existingTenantId, newTenantName, activationTokenHash, activationTokenExpiresAtUtc, startingRoles);

        public Task UserUpdateAsync(User user) => userRepository.UserUpdateAsync(user);

        public Task<bool> UserProfileSetAsync(string email, string? firstName, string? lastName, string? timeZone) =>
            userRepository.UserProfileSetAsync(email, firstName, lastName, timeZone);

        public Task<bool> UserSetDevicePinAsync(int idUser, string? devicePin, DateTime? expiresAtUtc) =>
            userRepository.UserSetDevicePinAsync(idUser, devicePin, expiresAtUtc);

        public Task<bool> UserDeleteAsync(int? idUser) => userRepository.UserDeleteAsync(idUser);

        public Task<User?> UserGetAsync(int? idUser, string? email, string? username) => userRepository.UserGetAsync(idUser, email, username);

        public Task<IList<User>> UsersGetAsync(int? tenantID) => userRepository.UsersGetAsync(tenantID);

        public Task<IList<User>> UsersGetAllAsync() => userRepository.UsersGetAllAsync();

        public Task<UserSecret?> UserSecretGetAsync(int? idUser, string? email, string? username) => userRepository.UserSecretGetAsync(idUser, email, username);

        public Task<bool> UserSetPasswordAsync(string? email, UserSecret userSecret) => userRepository.UserSetPasswordAsync(email, userSecret);

        public Task RevokeUserTokensAsync(int idUser) => userRepository.RevokeUserTokensAsync(idUser);

        public Task<IList<User>> TenantAdminsGetAsync(int tenantId) => userRepository.TenantAdminsGetAsync(tenantId);
    }
}
