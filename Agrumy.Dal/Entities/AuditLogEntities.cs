namespace api.Dal.Entities
{
    public class AuditLogRow
    {
        public int IDAuditLog { get; set; }
        public DateTime TimestampUtc { get; set; }
        public int? TenantID { get; set; }
        public int? ActorUserID { get; set; }
        public string? ActorEmail { get; set; }
        public string Action { get; set; } = "";
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }
        public string? Details { get; set; }
    }
}
