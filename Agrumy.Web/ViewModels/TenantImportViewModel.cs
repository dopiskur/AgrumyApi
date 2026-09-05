using System.ComponentModel.DataAnnotations;
using api.Models;

namespace api.ViewModels
{
    /// Import.cshtml's model - ExportJson is deserialized server-side (not posted as a typed object) so a malformed file surfaces as a validation error, not an MVC model-binding failure.
    public class TenantImportViewModel
    {
        [Required(ErrorMessage = "Paste an export file's contents, or choose a file above.")]
        public string? ExportJson { get; set; }

        [Required(ErrorMessage = "Target tenant name is required.")]
        public string? TargetTenantName { get; set; }

        // Set by the controller after a successful import so the view shows the result instead of re-rendering the form empty.
        public TenantImportResult? Result { get; set; }
    }
}
