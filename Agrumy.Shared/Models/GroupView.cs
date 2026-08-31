namespace api.Models
{
    public class GroupView
    {
        public UserGroup UserGroup { get; set; } = new();
        public IEnumerable<UserRole> UserRoles { get; set; } = [];
    }
}
