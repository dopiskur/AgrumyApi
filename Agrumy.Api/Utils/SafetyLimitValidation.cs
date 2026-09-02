namespace api.Utils
{
    /// <summary>Roadmap #36: shared bound for WaterPump's device-side hard safety limits (seconds) -
    /// used by both ServerConfigApiController (the server-wide default pair) and
    /// DeviceApiController (the per-device override pair) so the two save paths can never end up
    /// accepting different ranges for the exact same two fields. Null/0 is a real, intentional
    /// "disabled" state (see api.Models.DeviceConfigController's comment) - only a negative value
    /// or an upper bound obviously beyond any real watering cycle is rejected; this is a
    /// human-error catch, not an attempt to second-guess a legitimate long-running setup.</summary>
    public static class SafetyLimitValidation
    {
        public const int MaxReasonableSeconds = 86400; // 24h

        public static bool IsValid(int? seconds) => seconds is null or (>= 0 and <= MaxReasonableSeconds);
    }
}
