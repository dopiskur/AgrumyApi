namespace api.Dal.Entities
{
    // Persistence entities - mapped 1:1 to table columns. Kept separate from the api.Models DTOs,
    // which carry flattened join columns, MVC attributes and (for SensorData) no key. EfRepository
    // projects these rows onto the DTOs so the IRepository contract is unchanged.
    //
    // Column/type/nullability spec: this used to live in Schema/SchemaScripts.cs (deleted with the
    // stored-procedure DAL); the EF baseline migration is now the source of truth.

    public class TenantRow
    {
        public int IDTenant { get; set; }
        public string TenantName { get; set; } = "";
        public DateTime? DateCreated { get; set; }
    }

    public class UserRoleScopeRow
    {
        public int IDRoleScope { get; set; }
        public string? RoleScopeName { get; set; }
    }

    public class UserRoleRow
    {
        public int IDUserRole { get; set; }
        public string? RoleName { get; set; }
        public int? RoleScopeID { get; set; }
    }

    public class UserGroupRow
    {
        public int IDUserGroup { get; set; }
        public string? GroupName { get; set; }
        public int? UserRoleID { get; set; }
    }

    public class UserRow
    {
        public int IDUser { get; set; }
        public int TenantID { get; set; }
        public string Email { get; set; } = "";
        public string? Username { get; set; }
        public string PwdHash { get; set; } = "";
        public string PwdSalt { get; set; } = "";
        public int? DevicePin { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public int? UserGroupID { get; set; }
        public bool? Enabled { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? DateModified { get; set; }
    }

    public class ServerConfigRow
    {
        public int IDServerConfig { get; set; }
        public string? ServerConfigName { get; set; }
        public string ConfigKey { get; set; } = "";
        public string? JWTKey { get; set; }
        public int? PortHTTP { get; set; }
        public int? PortHTTPS { get; set; }
        public string? ServerConfigCol { get; set; }
    }
}
