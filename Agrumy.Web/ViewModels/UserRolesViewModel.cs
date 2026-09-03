namespace api.ViewModels
{
    public class UserRolesViewModel
    {
        public int IDUser { get; set; }
        public string? Email { get; set; }
        public IReadOnlyList<string> AllRoles { get; set; } = new List<string>();
        public List<string> AssignedRoles { get; set; } = new();
    }
}
