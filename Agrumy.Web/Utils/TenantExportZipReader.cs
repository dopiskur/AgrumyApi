using System.IO.Compression;
using api.Models;
using Microsoft.AspNetCore.Http;

namespace api.Utils
{
    /// Shared by TenantController.Import and LoginController.ImportSentinel - unpacks the ZIP TenantExportService.BuildExportZipAsync produces (Agrumy.Web has no reference to Agrumy.Api, so the unpacking lives here instead).
    public static class TenantExportZipReader
    {
        public static async Task<string> ReadExportJsonAsync(IFormFile file)
        {
            using var archive = new ZipArchive(file.OpenReadStream(), ZipArchiveMode.Read);
            ZipArchiveEntry? entry = archive.GetEntry(TenantExport.ExportEntryName);
            if (entry is null)
            {
                throw new InvalidDataException($"Missing {TenantExport.ExportEntryName}");
            }
            using StreamReader reader = new(entry.Open());
            return await reader.ReadToEndAsync();
        }
    }
}
