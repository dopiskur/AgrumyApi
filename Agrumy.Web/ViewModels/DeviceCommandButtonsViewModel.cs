using api.Models;

namespace api.ViewModels
{
    /// <summary>Roadmap #34: feeds _DeviceCommandButtons.cshtml, shared by Device Details, Zone
    /// detail (#116) and the Unit-level Zones() page (#81/#82) - TargetType/TargetId map straight
    /// onto IssueCommandRequest, resolved server-side by CommandQueueService.</summary>
    public class DeviceCommandButtonsViewModel
    {
        public required CommandTargetType TargetType { get; init; }
        public required int TargetId { get; init; }
    }
}
