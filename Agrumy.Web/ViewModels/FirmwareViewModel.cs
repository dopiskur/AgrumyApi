using api.Models;

namespace api.ViewModels
{
    /// <summary>Roadmap #94: the Firmware page - catalog rows plus the source settings that decide
    /// which of the population actions (refresh / pull / import / upload) make sense to show.</summary>
    public class FirmwareViewModel
    {
        public required ServerConfig Config { get; init; }
        public required IList<DeviceFirmware> Catalog { get; init; }
    }

    /// <summary>Roadmap #93: the per-device firmware card on Device Details - what's running, what
    /// the catalog offers for its board, and whether an update request is already pending.</summary>
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
