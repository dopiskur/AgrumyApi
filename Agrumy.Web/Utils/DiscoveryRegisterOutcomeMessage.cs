using api.Models;

namespace api.Utils
{
    /// Turns a Register outcome into the TempData message shown after the redirect, shared by every page that has a Register modal.
    public static class DiscoveryRegisterOutcomeMessage
    {
        public static (string? Message, string? Error) For(DiscoveryRegisterOutcome outcome) => outcome switch
        {
            DiscoveryRegisterOutcome.Success => ("Device registration sent to the scanning device.", null),
            DiscoveryRegisterOutcome.AlreadyPending => (null, "A registration is already pending for this device - wait for it to finish or expire."),
            DiscoveryRegisterOutcome.WifiCredentialsRequired => (null, "No saved WiFi network for your tenant - enter an SSID and password and try again."),
            DiscoveryRegisterOutcome.WifiConfigChoiceRequired => (null, "Multiple saved WiFi networks - pick one and try again."),
            _ => (null, "Registration could not be completed."),
        };
    }
}
