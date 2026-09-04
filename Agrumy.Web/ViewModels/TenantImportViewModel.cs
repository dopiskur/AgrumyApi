using System.ComponentModel.DataAnnotations;
using api.Models;

namespace api.ViewModels
{
    /// <summary>Import.cshtml's model. ExportJson is pasted/loaded-from-file text,
    /// deserialized server-side rather than posted as a typed object so a malformed file surfaces
    /// as a normal validation error instead of an MVC model-binding failure.</summary>
    public class TenantImportViewModel
    {
        [Required(ErrorMessage = "Paste an export file's contents, or choose a file above.")]
        public string? ExportJson { get; set; }

        [Required(ErrorMessage = "Target tenant name is required.")]
        public string? TargetTenantName { get; set; }

        // Set by the controller after a successful import - the view shows this instead of
        // re-rendering the form empty, so the admin sees exactly what happened.
        public TenantImportResult? Result { get; set; }
    }
}
