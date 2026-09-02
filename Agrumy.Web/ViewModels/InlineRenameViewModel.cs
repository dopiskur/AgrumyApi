namespace api.ViewModels
{
    /// <summary>Roadmap #116 rule (2): a `&lt;details&gt;`-based inline rename control (no JS -
    /// clicking the summary reveals the form) shared by Unit and Zone names, one id field either
    /// way (UnitRename needs idDeviceUnit, ZoneRename needs idDeviceUnitZone).</summary>
    public class InlineRenameViewModel
    {
        public required string Action { get; init; }
        public required string IdFieldName { get; init; }
        public required int IdValue { get; init; }
        public required string NameFieldName { get; init; }
        public string? CurrentName { get; init; }
        public int MaxLength { get; init; } = 100;
    }
}
