using System.ComponentModel.DataAnnotations;

namespace api.ViewModels
{
    /// <summary>LoginController.ImportSentinel's model. No target-name field
    /// (unlike TenantImportViewModel) - AsSentinel always means TenantID=0.</summary>
    public class TenantSentinelImportViewModel
    {
        [Required(ErrorMessage = "Paste an export file's contents, or choose a file above.")]
        public string? ExportJson { get; set; }
    }
}
