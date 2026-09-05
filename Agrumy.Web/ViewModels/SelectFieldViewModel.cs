using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.ViewModels
{
    public class SelectFieldViewModel
    {
        /// Full form field name the select posts under, e.g. "DeviceConfigController.Relays[0].RelayFunction" - same value asp-for would generate.
        public required string Name { get; init; }

        public required string Label { get; init; }

        public required IEnumerable<SelectListItem> Items { get; init; }
    }
}
