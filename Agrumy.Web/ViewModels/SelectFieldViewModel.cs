using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.ViewModels
{
    public class SelectFieldViewModel
    {
        /// <summary>Full form field name the select posts under, e.g. "DeviceConfigController.Relay1" - same value asp-for would generate.</summary>
        public required string Name { get; init; }

        public required string Label { get; init; }

        public required IEnumerable<SelectListItem> Items { get; init; }
    }
}
