namespace api.ViewModels
{
    /// <summary>Render model for the shared _EnabledToggleField partial - collapses the
    /// Enabled/Disabled button-group radio pair that was copy-pasted identically 11 times across
    /// Device and User forms (only the field name and label ever changed).</summary>
    public class EnabledToggleFieldViewModel
    {
        /// <summary>Full form field name the pair posts under, e.g.
        /// "DeviceConfigController.RelayEnabled" - same value asp-for would generate.</summary>
        public required string Name { get; init; }

        public required string Label { get; init; }

        public bool? Value { get; init; }
    }
}
