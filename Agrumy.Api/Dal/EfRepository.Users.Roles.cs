using api.Models;

namespace api.Dal
{
    /// IUserRepository role members - forwarded to the standalone EfUserRepository (roadmap #246) so IRepository's broad consumers keep working unchanged.
    internal partial class EfRepository
    {
        public Task<IList<UserRole>> UserRoleGetAsync() => userRepository.UserRoleGetAsync();

        public Task<IReadOnlyList<string>> UserRoleNamesGetAsync(int idUser) => userRepository.UserRoleNamesGetAsync(idUser);

        public Task UserRolesSetAsync(int idUser, IEnumerable<string> roleNames) => userRepository.UserRolesSetAsync(idUser, roleNames);
    }
}
