using System.ComponentModel.DataAnnotations;

namespace api.Models
{
    /// <summary>One recorded admin-facing action - who changed another account's access/state, when, and to what. Write-once: nothing ever updates or deletes a row.</summary>
    public class AuditLogEntry
    {
        public int IDAuditLog { get; set; }
        public DateTime TimestampUtc { get; set; }

        /// <summary>Null for a cross-tenant action taken by a Global admin.</summary>
        public int? TenantID { get; set; }

        /// <summary>Null if the actor's own account was later deleted - ActorEmail is the durable record.</summary>
        public int? ActorUserID { get; set; }
        [MaxLength(255)]
        public string? ActorEmail { get; set; }

        /// <summary>Short machine-readable tag, e.g. "User.RolesChanged", "User.Deleted", "User.EnabledChanged".</summary>
        [MaxLength(100)]
        public string Action { get; set; } = "";

        [MaxLength(50)]
        public string? TargetType { get; set; }
        [MaxLength(50)]
        public string? TargetId { get; set; }

        /// <summary>Free-text summary, e.g. "TenantReader -> TenantAdmin". Not for secrets - this is readable by every tenant admin who can see the row.</summary>
        public string? Details { get; set; }
    }
}
