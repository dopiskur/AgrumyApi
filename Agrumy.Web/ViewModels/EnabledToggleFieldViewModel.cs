namespace api.ViewModels
{
    public class EnabledToggleFieldViewModel
    {
        /// Full form field name the pair posts under, e.g. "DeviceConfigController.RelayEnabled" - same value asp-for would generate.
        public required string Name { get; init; }

        public required string Label { get; init; }

        public bool? Value { get; init; }
    }
}
