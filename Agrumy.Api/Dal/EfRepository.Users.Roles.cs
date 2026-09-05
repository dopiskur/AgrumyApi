using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// IUserRepository members: the fixed role list and composable roles.
    internal partial class EfRepository
    {
        public async Task<IList<UserRole>> UserRoleGetAsync()
        {
            return await db.UserRoles.AsNoTracking()
                .Select(r => new UserRole { IDUserRole = r.IDUserRole, RoleName = r.RoleName, RoleScopeID = r.RoleScopeID })
                .ToListAsync();
        }

        public async Task<IReadOnlyList<string>> UserRoleNamesGetAsync(int idUser)
        {
            return await (from ur in db.UserUserRoles.AsNoTracking()
                          join r in db.UserRoles.AsNoTracking() on ur.UserRoleID equals r.IDUserRole
                          where ur.UserID == idUser && r.RoleName != null
                          select r.RoleName!).ToListAsync();
        }

        public async Task UserRolesSetAsync(int idUser, IEnumerable<string> roleNames)
        {
            var wanted = roleNames.ToHashSet();

            var roleIds = await db.UserRoles.AsNoTracking()
                .Where(r => r.RoleName != null && wanted.Contains(r.RoleName))
                .Select(r => r.IDUserRole)
                .ToListAsync();

            var existing = await db.UserUserRoles.Where(x => x.UserID == idUser).ToListAsync();
            db.UserUserRoles.RemoveRange(existing.Where(x => !roleIds.Contains(x.UserRoleID)));
            db.UserUserRoles.AddRange(roleIds
                .Where(id => existing.All(x => x.UserRoleID != id))
                .Select(id => new UserUserRoleRow { UserID = idUser, UserRoleID = id }));

            await db.SaveChangesAsync();
        }
    }
}
