using api.Models;

namespace api.ViewModels
{
    public class DeviceCommandButtonsViewModel
    {
        public required CommandTargetType TargetType { get; init; }
        public required int TargetId { get; init; }
    }
}
