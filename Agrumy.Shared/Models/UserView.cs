namespace api.Models
{
    public class UserView
    {
        public UserUpdate? UserUpdate { get; set; } = new UserUpdate();
        public UserAdd? UserAdd { get; set; } = new UserAdd();
        public IEnumerable<UserGroup>? UserGroups { get; set; }


    }

}
