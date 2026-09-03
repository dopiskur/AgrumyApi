namespace api.Models
{
    public enum CommandActionType
    {
        Reboot = 1,
        ForceOTA = 2,
        ForceConfigSync = 3,
    }

    /// <summary>Device acknowledges before executing, so a command stuck at Acknowledged (never reaching Executed) means it took the command but crashed or lost power before confirming the outcome.</summary>
    public enum CommandStatus
    {
        Pending = 0,
        Acknowledged = 1,
        Executed = 2,
        Expired = 3,
    }

    /// <summary>Target resolved server-side to the device(s) implied by Zone/Unit; see CommandQueueService.IssueCommandAsync.</summary>
    public enum CommandTargetType
    {
        Device = 1,
        Zone = 2,
        Unit = 3,
    }

    /// <summary>Body of POST /api/DeviceCommand.</summary>
    public class IssueCommandRequest
    {
        public CommandTargetType TargetType { get; set; }
        public int TargetId { get; set; }
        public CommandActionType ActionType { get; set; }
    }

    public class DeviceCommand
    {
        public int IDDeviceCommand { get; set; }
        public int DeviceID { get; set; }
        public CommandActionType ActionType { get; set; }
        public CommandStatus Status { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
    }

    /// <summary>Minimal shape returned inside DeviceConfig during a device's regular config poll — not a separate endpoint.</summary>
    public class PendingCommand
    {
        public int IDDeviceCommand { get; set; }
        public CommandActionType ActionType { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>Body of POST /api/Device/Command/Ack.</summary>
    public class CommandAckRequest
    {
        public int CommandId { get; set; }
    }
}
