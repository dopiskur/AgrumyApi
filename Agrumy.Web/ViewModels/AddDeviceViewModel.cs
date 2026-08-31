namespace api.ViewModels
{
    /// <summary>Roadmap #76: the PIN shown on the "Add New Device" page - either the caller's
    /// still-valid one or a freshly generated one, the view can't tell which and doesn't need to.</summary>
    public class AddDeviceViewModel
    {
        public string? DevicePin { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
