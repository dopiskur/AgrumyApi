namespace api.ViewModels
{
    /// <summary>Roadmap #66: render/post model for editing one user's composable role set - a plain
    /// checkbox group (name="AssignedRoles") binds straight into a List&lt;string&gt; on postback.</summary>
    public class UserRolesViewModel
    {
        public int IDUser { get; set; }
        public string? Email { get; set; }
        public IReadOnlyList<string> AllRoles { get; set; } = new List<string>();
        public List<string> AssignedRoles { get; set; } = new();
    }
}
