using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>IUserRepository members (roadmap #113 split, continuing #74): user group CRUD.</summary>
    internal partial class EfRepository
    {
        // ---- Group ---------------------------------------------------------

        public async Task<IList<UserGroup>> UserGroupsGetAsync()
        {
            return await (from g in db.UserGroups.AsNoTracking()
                          join r in db.UserRoles.AsNoTracking() on g.UserRoleID equals r.IDUserRole
                          select new UserGroup
                          {
                              IDUserGroup = g.IDUserGroup,
                              GroupName = g.GroupName,
                              UserRoleID = g.UserRoleID,
                              RoleName = r.RoleName,
                          }).ToListAsync();
        }

        public async Task<UserGroup?> UserGroupGetAsync(int? idUserGroup)
        {
            return await (from g in db.UserGroups.AsNoTracking()
                          join r in db.UserRoles.AsNoTracking() on g.UserRoleID equals r.IDUserRole
                          where g.IDUserGroup == idUserGroup
                          select new UserGroup
                          {
                              IDUserGroup = g.IDUserGroup,
                              GroupName = g.GroupName,
                              UserRoleID = g.UserRoleID,
                              RoleName = r.RoleName,
                          }).FirstOrDefaultAsync();
        }

        public async Task UserGroupDeleteAsync(int? idUserGroup)
        {
            if (idUserGroup is null or <= 0)
            {
                return; // proc guard: IF (idUserGroup > 0)
            }
            await db.UserGroups.Where(g => g.IDUserGroup == idUserGroup).ExecuteDeleteAsync();
        }

        public async Task UserGroupAddAsync(UserGroup userGroup)
        {
            db.UserGroups.Add(new UserGroupRow
            {
                GroupName = userGroup.GroupName,
                UserRoleID = userGroup.UserRoleID,
            });
            await db.SaveChangesAsync();
        }
    }
}
