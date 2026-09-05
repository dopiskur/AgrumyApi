namespace api.Utils
{
    /// Shared bound for WaterPump's device-side hard safety limits (seconds), used by both ServerConfigApiController and DeviceApiController so the two save paths can't accept different ranges; null/0 means intentionally disabled.
    public static class SafetyLimitValidation
    {
        public const int MaxReasonableSeconds = 86400; // 24h

        public static bool IsValid(int? seconds) => seconds is null or (>= 0 and <= MaxReasonableSeconds);
    }
}
