using Microsoft.AspNetCore.Http;

namespace api.ViewModels
{
    /// LoginController.ImportSentinel's model - no target-name field since AsSentinel always means TenantID=0. Exactly one of ExportFile/ExportJson is expected - LoginController.ImportSentinel enforces that, same reasoning as TenantImportViewModel.
    public class TenantSentinelImportViewModel
    {
        // The ZIP TenantController.Export now produces (see TenantExportService.BuildExportZipAsync) - preferred over ExportJson when both are somehow present.
        public IFormFile? ExportFile { get; set; }

        // Manual paste path, kept for a hand-edited/older plain-JSON export.
        public string? ExportJson { get; set; }
    }
}
