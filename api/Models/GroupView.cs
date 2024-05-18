namespace api.Models
{
    public class GroupView
    {
        public UserGroup UserGroup { get; set; }
        public IEnumerable<UserRole> UserRoles { get; set; }
    }
}
