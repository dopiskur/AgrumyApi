namespace api.Models
{
    /// <summary>Roadmap #34: discrete, stateless, one-shot device actions - deliberately narrow
    /// scope (see the roadmap's own PROBLEM 1 resolution note: anything with duration/ongoing
    /// state belongs to #35's manual override, not here). SelfTest was considered in the original
    /// design note but is excluded from v1 - no device-side concept of a self-test exists yet, and
    /// the user confirmed to leave it out without further discussion.</summary>
    public enum CommandActionType
    {
        Reboot = 1,
        ForceOTA = 2,
        ForceConfigSync = 3,
    }

    /// <summary>Roadmap #34: real FIFO queue state per command (not a single "last value wins"
    /// counter - see the roadmap's PROBLEM 2 resolution). Acknowledged is a real, observable state
    /// (not just an implementation detail) because the device is required to ack BEFORE it
    /// executes - a command that reads Acknowledged but never reaches Executed means the device
    /// took the command and then never confirmed the outcome (crashed, lost power, or - for
    /// Reboot - has nothing further to report).</summary>
    public enum CommandStatus
    {
        Pending = 0,
        Acknowledged = 1,
        Executed = 2,
        Expired = 3,
    }

    /// <summary>Roadmap #34: what POST /api/DeviceCommand issues a command AGAINST - a single
    /// device directly, or resolved server-side to the device(s) implied by a Zone/Unit (see
    /// CommandQueueService.IssueCommandAsync for the fan-out rules, #82's "one controller per
    /// zone" invariant is what makes Zone resolve to at most one device).</summary>
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

    /// <summary>One row from GET-style admin views of the command log (not currently exposed via
    /// its own endpoint - the Web UI's own issue-confirmation is enough for v1 - but kept as a
    /// full DTO since api.Dal.EfRepository.Commands.cs already builds it internally).</summary>
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

    /// <summary>Roadmap #34: the minimal shape ridden along in DeviceConfig (the SAME response the
    /// device already gets from every Config poll - deliberately not a new endpoint, see
    /// DeviceApiController.GetConfig) - just enough for the firmware to know what to ack/execute,
    /// nothing an admin-facing DeviceCommand DTO carries that the device has no use for.</summary>
    public class PendingCommand
    {
        public int IDDeviceCommand { get; set; }
        public CommandActionType ActionType { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>Body of POST /api/Device/Command/Ack - the device confirms receipt of exactly one
    /// PendingCommand BEFORE executing it (roadmap #34: must happen before, not after, since a
    /// Reboot action has no "after" on the same connection to report from).</summary>
    public class CommandAckRequest
    {
        public int CommandId { get; set; }
    }
}
