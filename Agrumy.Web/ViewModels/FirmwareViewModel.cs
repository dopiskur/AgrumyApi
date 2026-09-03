using api.Models;

namespace api.ViewModels
{
    public class FirmwareViewModel
    {
        public required ServerConfig Config { get; init; }
        public required IList<DeviceFirmware> Catalog { get; init; }
        public required IList<DeviceFirmware> InstallableBoards { get; init; }
    }

    public class DeviceFirmwareViewModel
    {
        public required int IdDevice { get; init; }
        public string? Board { get; init; }
        public string? RunningVersion { get; init; }
        public string? LatestVersion { get; init; }
        public bool UpdateAvailable { get; init; }
        public bool UpdatePending { get; init; }
        public string? TargetVersion { get; init; }
        public IList<DeviceFirmware> Versions { get; init; } = [];
    }
}
