using System.ComponentModel.DataAnnotations;
using api.Models;
using Microsoft.AspNetCore.Http;

namespace api.ViewModels
{
    /// Import.cshtml's model - ExportJson/ExportFile are unpacked/deserialized server-side (not posted as a typed object) so a malformed file surfaces as a validation error, not an MVC model-binding failure. Exactly one of the two is expected - TenantController.Import enforces that, not a DataAnnotation, since "at least one of these two" isn't expressible as a single [Required].
    public class TenantImportViewModel
    {
        // The ZIP TenantController.Export now produces (see TenantExportService.BuildExportZipAsync) - preferred over ExportJson when both are somehow present.
        public IFormFile? ExportFile { get; set; }

        // Manual paste path, kept for a hand-edited/older plain-JSON export.
        public string? ExportJson { get; set; }

        [Required(ErrorMessage = "Target tenant name is required.")]
        public string? TargetTenantName { get; set; }

        // Set by the controller after a successful import so the view shows the result instead of re-rendering the form empty.
        public TenantImportResult? Result { get; set; }
    }
}
