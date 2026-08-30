using Microsoft.AspNetCore.Mvc.Rendering;

namespace api.ViewModels
{
    /// <summary>Render model for the shared _SelectField partial - collapses the "label + dropdown
    /// bound by form field name" markup that used to be copy-pasted once per DropDownListFor call
    /// (8x for relay slots, 13x for sensor slots, plus every other single-select field in the app).</summary>
    public class SelectFieldViewModel
    {
        /// <summary>Full form field name the select posts under, e.g. "DeviceConfigController.Relay1" -
        /// same value asp-for would generate from the bound expression.</summary>
        public required string Name { get; init; }

        public required string Label { get; init; }

        /// <summary>Pre-built so callers keep control over value/text field names and the
        /// currently-selected item, exactly as the old inline `new SelectList(...)` calls did.</summary>
        public required IEnumerable<SelectListItem> Items { get; init; }
    }
}
