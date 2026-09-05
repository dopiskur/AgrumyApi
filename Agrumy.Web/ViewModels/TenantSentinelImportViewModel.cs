using System.ComponentModel.DataAnnotations;

namespace api.ViewModels
{
    /// LoginController.ImportSentinel's model - no target-name field since AsSentinel always means TenantID=0.
    public class TenantSentinelImportViewModel
    {
        [Required(ErrorMessage = "Paste an export file's contents, or choose a file above.")]
        public string? ExportJson { get; set; }
    }
}
